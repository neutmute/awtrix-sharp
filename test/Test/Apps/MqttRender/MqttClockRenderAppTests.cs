using System.Reflection;
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Test.Apps.MqttRender
{
    public class MqttClockRenderAppTests
    {
        private Mock<ILogger> _mockLogger;
        private Mock<IAwtrixService> _mockAwtrixService;
        private Mock<IMqttConnector> _mockMqttConnector;
        private Mock<ITimerService> _mockTimerService;
        private AwtrixAddress _address;
        private FakeTimeProvider _time;
        private IClock _clock;
        private MqttAppConfig _config;

        private MqttClockRenderApp CreateSut(string readTopic = "read/topic")
        {
            _mockLogger = new Mock<ILogger>();
            _mockAwtrixService = new Mock<IAwtrixService>();
            _mockMqttConnector = new Mock<IMqttConnector>();
            _mockTimerService = new Mock<ITimerService>();
            _address = new AwtrixAddress { BaseTopic = "test/base/topic" };
            // Pinned instant (CR-39); FakeTimeProvider drives ActiveTime so windows can be ended deterministically
            _time = new FakeTimeProvider(new DateTimeOffset(2025, 1, 1, 8, 0, 0, TimeSpan.Zero));
            _clock = new Clock(_time);

            _mockAwtrixService.Setup(x => x.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>())).ReturnsAsync(true);
            _mockAwtrixService.Setup(x => x.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>())).ReturnsAsync(true);
            _mockAwtrixService.Setup(x => x.Dismiss(It.IsAny<AwtrixAddress>())).ReturnsAsync(true);
            _mockMqttConnector.Setup(x => x.Subscribe(It.IsAny<string>())).Returns(Task.CompletedTask);

            _config = new MqttAppConfig
            {
                CronSchedule = "* * * * *",
                ActiveTime = TimeSpan.FromMinutes(5),
                ReadTopic = readTopic
            };
            _config.WithName("MqttClockRenderApp");

            return new MqttClockRenderApp(
                _mockLogger.Object, _clock, _config, _address, _mockAwtrixService.Object, _mockMqttConnector.Object, _mockTimerService.Object);
        }

        [Fact]
        public void SecondChanged_UpdatesDisplayWithFormattedClockAndEmptyValue()
        {
            var sut = CreateSut();
            sut.ExecuteNow();

            var tickTime = new DateTime(2025, 1, 1, 6, 30, 0);
            _mockTimerService.Raise(x => x.SecondChanged += null, this, new ClockTickEventArgs(tickTime));

            var expectedClock = TimerService.FormatClockString(tickTime, true);
            _mockAwtrixService.Verify(x => x.AppUpdate(
                _address,
                "MqttClockRenderApp",
                It.Is<AwtrixAppMessage>(m => m.Text == $"{expectedClock} ")), Times.Once);

            sut.Dispose();
        }

        [Fact]
        public void MessageReceived_UpdatesDisplayWithClockAndMqttValue()
        {
            var sut = CreateSut();
            sut.ExecuteNow();

            var tickTime = new DateTime(2025, 1, 1, 6, 30, 0);
            _mockTimerService.Raise(x => x.SecondChanged += null, this, new ClockTickEventArgs(tickTime));

            var args = MqttTestHelpers.CreateReceivedArgs("read/topic", "22.5C");
            _mockMqttConnector.Raise(x => x.MessageReceived += null, new object[] { args });

            var expectedClock = TimerService.FormatClockString(tickTime, true);
            _mockAwtrixService.Verify(x => x.AppUpdate(
                _address,
                "MqttClockRenderApp",
                It.Is<AwtrixAppMessage>(m => m.Text == $"{expectedClock} 22.5C")), Times.Once);

            sut.Dispose();
        }

        [Fact]
        public void MessageReceived_SetsLongDuration()
        {
            var sut = CreateSut();
            sut.ExecuteNow();

            var args = MqttTestHelpers.CreateReceivedArgs("read/topic", "22.5C");
            _mockMqttConnector.Raise(x => x.MessageReceived += null, new object[] { args });

            _mockAwtrixService.Verify(x => x.AppUpdate(
                _address,
                "MqttClockRenderApp",
                It.Is<AwtrixAppMessage>(m => m["duration"] == "3600")), Times.Once);

            sut.Dispose();
        }

        [Fact]
        public void ExecuteNow_RetainedMessageDeliveredDuringSubscribe_IsRendered()
        {
            var sut = CreateSut("read/topic");
            _mockMqttConnector
                .Setup(x => x.Subscribe("read/topic"))
                .Callback<string>(topic => _mockMqttConnector.Raise(
                    x => x.MessageReceived += null,
                    new object[] { MqttTestHelpers.CreateReceivedArgs(topic, "22.5C") }))
                .Returns(Task.CompletedTask);

            sut.ExecuteNow();

            _mockAwtrixService.Verify(x => x.AppUpdate(
                _address,
                "MqttClockRenderApp",
                It.Is<AwtrixAppMessage>(m => m.Text != null && m.Text.EndsWith(" 22.5C"))), Times.Once);

            sut.Dispose();
        }

        private async Task EndWindowAsync(MqttClockRenderApp sut)
        {
            _time.Advance(_config.ActiveTime);
            await sut.LastRun.WaitAsync(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task AfterActiveTimeEnds_ClockTicksNoLongerPublish()
        {
            // CR-09: SecondChanged was never unsubscribed, so the slot was republished a second after being cleared
            var sut = CreateSut();
            sut.ExecuteNow();
            await EndWindowAsync(sut);
            _mockAwtrixService.Invocations.Clear();

            _mockTimerService.Raise(x => x.SecondChanged += null, this, new ClockTickEventArgs(new DateTime(2025, 1, 1, 8, 5, 1)));

            _mockAwtrixService.Verify(x => x.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()), Times.Never);
            _mockTimerService.VerifyRemove(x => x.SecondChanged -= It.IsAny<EventHandler<ClockTickEventArgs>>(), Times.Once);
        }

        [Fact]
        public async Task RepeatedActivations_AttachOnlyOneClockTickHandler()
        {
            // CR-09 failure scenario: after N daily activations the clock received N publishes per second
            var sut = CreateSut();
            for (var day = 0; day < 3; day++)
            {
                sut.ExecuteNow();
                await EndWindowAsync(sut);
            }
            sut.ExecuteNow();
            _mockAwtrixService.Invocations.Clear();

            _mockTimerService.Raise(x => x.SecondChanged += null, this, new ClockTickEventArgs(new DateTime(2025, 1, 1, 8, 20, 1)));

            _mockAwtrixService.Verify(x => x.AppUpdate(_address, "MqttClockRenderApp", It.IsAny<AwtrixAppMessage>()), Times.Once);
            await sut.DisposeAsync();
        }

        [Fact]
        public async Task ClockTickDeliveredAfterDeactivation_IsIgnored()
        {
            // TimerService snapshots its invocation list, so one tick can arrive after unsubscribe
            var sut = CreateSut();
            sut.ExecuteNow();
            await EndWindowAsync(sut);
            _mockAwtrixService.Invocations.Clear();

            var tick = typeof(MqttClockRenderApp).GetMethod("ClockTick", BindingFlags.NonPublic | BindingFlags.Instance)!;
            tick.Invoke(sut, new object?[] { null, new ClockTickEventArgs(new DateTime(2025, 1, 1, 8, 5, 1)) });

            _mockAwtrixService.Verify(x => x.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()), Times.Never);
        }
    }
}
