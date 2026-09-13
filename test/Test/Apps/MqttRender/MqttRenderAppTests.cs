using System.Reflection;
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using MQTTnet;

namespace Test.Apps.MqttRender
{
    public class MqttRenderAppTests
    {
        private Mock<ILogger> _mockLogger;
        private Mock<IAwtrixService> _mockAwtrixService;
        private Mock<IMqttConnector> _mockMqttConnector;
        private AwtrixAddress _address;
        private FakeTimeProvider _time;
        private IClock _clock;
        private MqttAppConfig _config;

        private AwtrixSharpWeb.Apps.MqttRender.MqttRenderApp CreateSut(string readTopic = "read/topic")
        {
            _mockLogger = new Mock<ILogger>();
            _mockAwtrixService = new Mock<IAwtrixService>();
            _mockMqttConnector = new Mock<IMqttConnector>();
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
            _config.WithName("MqttRenderApp");

            return new AwtrixSharpWeb.Apps.MqttRender.MqttRenderApp(
                _mockLogger.Object, _clock, _config, _address, _mockAwtrixService.Object, _mockMqttConnector.Object);
        }

        [Fact]
        public void ExecuteNow_SubscribesToConfiguredTopic()
        {
            var sut = CreateSut("read/topic");

            sut.ExecuteNow();

            _mockMqttConnector.Verify(x => x.Subscribe("read/topic"), Times.Once);

            sut.Dispose();
        }

        [Fact]
        public void MessageReceived_OnMatchingTopic_UpdatesAppWithText()
        {
            var sut = CreateSut("read/topic");
            sut.ExecuteNow();

            var args = MqttTestHelpers.CreateReceivedArgs("read/topic", "hello world");
            _mockMqttConnector.Raise(x => x.MessageReceived += null, new object[] { args });

            _mockAwtrixService.Verify(x => x.AppUpdate(
                _address,
                "MqttRenderApp",
                It.Is<AwtrixAppMessage>(m => m.Text == "hello world")), Times.Once);

            sut.Dispose();
        }

        [Fact]
        public void MessageReceived_OnDifferentTopic_IsIgnored()
        {
            var sut = CreateSut("read/topic");
            sut.ExecuteNow();

            var args = MqttTestHelpers.CreateReceivedArgs("other/topic", "hello world");
            _mockMqttConnector.Raise(x => x.MessageReceived += null, new object[] { args });

            _mockAwtrixService.Verify(x => x.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()), Times.Never);

            sut.Dispose();
        }

        [Fact]
        public void MessageReceived_WithMatchingValueMap_DecoratesMessage()
        {
            var sut = CreateSut("read/topic");
            _config.ValueMaps.Add(new ValueMap { { "ValueMatcher", "busy" }, { "Icon", "12345" } });
            sut.ExecuteNow();

            var args = MqttTestHelpers.CreateReceivedArgs("read/topic", "busy");
            _mockMqttConnector.Raise(x => x.MessageReceived += null, new object[] { args });

            _mockAwtrixService.Verify(x => x.AppUpdate(
                _address,
                "MqttRenderApp",
                It.Is<AwtrixAppMessage>(m => m["icon"] == "12345")), Times.Once);

            sut.Dispose();
        }

        [Fact]
        public void MessageReceived_WithValueMapThatSetsNoText_FallsBackToRawPayload()
        {
            var sut = CreateSut("read/topic");
            _config.ValueMaps.Add(new ValueMap { { "ValueMatcher", "busy" }, { "Icon", "12345" } });
            sut.ExecuteNow();

            var args = MqttTestHelpers.CreateReceivedArgs("read/topic", "busy");
            _mockMqttConnector.Raise(x => x.MessageReceived += null, new object[] { args });

            _mockAwtrixService.Verify(x => x.AppUpdate(
                _address,
                "MqttRenderApp",
                It.Is<AwtrixAppMessage>(m => m.Text == "busy")), Times.Once);

            sut.Dispose();
        }

        [Fact]
        public void ExecuteNow_RetainedMessageDeliveredDuringSubscribe_IsRendered()
        {
            var sut = CreateSut("read/topic");
            // Simulate the broker delivering a retained message before Subscribe returns (CR-33)
            _mockMqttConnector
                .Setup(x => x.Subscribe("read/topic"))
                .Callback<string>(topic => _mockMqttConnector.Raise(
                    x => x.MessageReceived += null,
                    new object[] { MqttTestHelpers.CreateReceivedArgs(topic, "retained value") }))
                .Returns(Task.CompletedTask);

            sut.ExecuteNow();

            _mockAwtrixService.Verify(x => x.AppUpdate(
                _address,
                "MqttRenderApp",
                It.Is<AwtrixAppMessage>(m => m.Text == "retained value")), Times.Once);

            sut.Dispose();
        }

        private async Task EndWindowAsync(AwtrixSharpWeb.Apps.MqttRender.MqttRenderApp sut)
        {
            _time.Advance(_config.ActiveTime);
            await sut.LastRun.WaitAsync(TimeSpan.FromSeconds(5));
            _mockAwtrixService.Invocations.Clear();
        }

        private int MessageHandlerAdds() => _mockMqttConnector.Invocations.Count(i => i.Method.Name == "add_MessageReceived");

        [Fact]
        public async Task MessageAfterActiveTimeEnds_IsNotRendered()
        {
            var sut = CreateSut("read/topic");
            sut.ExecuteNow();
            await EndWindowAsync(sut);

            _mockMqttConnector.Raise(x => x.MessageReceived += null,
                new object[] { MqttTestHelpers.CreateReceivedArgs("read/topic", "late") });

            _mockAwtrixService.Verify(x => x.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()), Times.Never);
            _mockMqttConnector.VerifyRemove(x => x.MessageReceived -= It.IsAny<Func<MqttApplicationMessageReceivedEventArgs, Task>>(), Times.Once);
        }

        [Fact]
        public async Task MessageDispatchedAfterDeactivation_IsIgnored()
        {
            // The connector snapshots its handler list, so a dispatch in flight can reach a detached handler
            var sut = CreateSut("read/topic");
            sut.ExecuteNow();
            await EndWindowAsync(sut);

            var handler = typeof(AwtrixSharpWeb.Apps.MqttRender.MqttRenderApp)
                .GetMethod("RawMessageReceived", BindingFlags.NonPublic | BindingFlags.Instance)!;
            await (Task)handler.Invoke(sut, new object[] { MqttTestHelpers.CreateReceivedArgs("read/topic", "late") })!;

            _mockAwtrixService.Verify(x => x.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()), Times.Never);
        }

        [Fact]
        public async Task SubscribeThatNeverCompletes_DoesNotOutliveTheWindow()
        {
            var sut = CreateSut("read/topic");
            _mockMqttConnector.Setup(x => x.Subscribe("read/topic")).Returns(new TaskCompletionSource().Task);
            sut.ExecuteNow();

            _time.Advance(_config.ActiveTime);

            await sut.LastRun.WaitAsync(TimeSpan.FromSeconds(5));
            _mockMqttConnector.VerifyRemove(x => x.MessageReceived -= It.IsAny<Func<MqttApplicationMessageReceivedEventArgs, Task>>(), Times.Once);
        }

        [Fact]
        public async Task ExecuteNow_WhileAlreadyActive_EndsAfterActiveTime_AndRearmsTheCron()
        {
            // WS3 review M1: POST api/app/MqttRender/start on an active app used to start a window that never
            // ended, and the cron never fired again until restart
            var sut = CreateSut("read/topic");
            await sut.InitAsync();
            sut.ExecuteNow();
            sut.ExecuteNow();
            Assert.True(SpinWait.SpinUntil(() => MessageHandlerAdds() == 2, TimeSpan.FromSeconds(5))); // #2 wired after #1's teardown

            _time.Advance(_config.ActiveTime);
            await sut.LastRun.WaitAsync(TimeSpan.FromSeconds(5));

            _mockMqttConnector.VerifyRemove(x => x.MessageReceived -= It.IsAny<Func<MqttApplicationMessageReceivedEventArgs, Task>>(), Times.Exactly(2));
            Assert.Equal(new DateTimeOffset(2025, 1, 1, 8, 6, 0, TimeSpan.Zero), sut.NextWakeUp);
            await sut.DisposeAsync();
        }
    }
}
