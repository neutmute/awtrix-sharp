using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.Diurnal;
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Apps.SlackStatus;
using AwtrixSharpWeb.Apps.TripTimer;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MQTTnet;
using Test.Apps.MqttRender;

namespace Test.Apps
{
    /// <summary>
    /// CR-31: every app releases its event subscriptions on dispose, and the synchronous Dispose never blocks.
    /// </summary>
    public class AppDisposalTests
    {
        private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);
        private static readonly DateTimeOffset Noon = new(2026, 9, 13, 12, 0, 0, TimeSpan.FromHours(10));

        private static AwtrixAddress Address() => new() { BaseTopic = "awtrix/clock1" };

        [Fact]
        public async Task DiurnalApp_DisposeAsync_UnsubscribesMinuteChanged()
        {
            var timer = new Mock<ITimerService>();
            // WS5: an empty schedule no longer subscribes, so give Diurnal one valid entry
            var config = AppConfig.Empty().WithName(AppNames.DiurnalApp);
            config.Config["0600"] = "Brightness=8";
            var app = new DiurnalApp(NullLogger.Instance, new MockClock(Noon), timer.Object,
                config, Address(), new Mock<IAwtrixService>().Object);
            await app.InitAsync();

            await app.DisposeAsync();

            timer.VerifyAdd(t => t.MinuteChanged += It.IsAny<EventHandler<ClockTickEventArgs>>(), Times.Once);
            timer.VerifyRemove(t => t.MinuteChanged -= It.IsAny<EventHandler<ClockTickEventArgs>>(), Times.Once);
        }

        [Fact]
        public async Task SlackStatusApp_DisposeAsync_UnsubscribesUserStatusChanged()
        {
            var slack = new Mock<ISlackConnector>();
            var config = new SlackStatusAppConfig();
            config.WithName(AppNames.SlackStatusApp);
            var app = new SlackStatusApp(NullLogger.Instance, config, Address(), new Mock<IAwtrixService>().Object, slack.Object);
            await app.InitAsync();

            await app.DisposeAsync();

            slack.VerifyRemove(s => s.UserStatusChanged -= It.IsAny<EventHandler<SlackUserStatusChangedEventArgs>>(), Times.Once);
        }

        [Fact]
        public async Task ButtonApp_DisposeAsync_DetachesMessageHandler_SoButtonsNoLongerClick()
        {
            var mqtt = new Mock<IMqttConnector>();
            mqtt.Setup(m => m.Subscribe(It.IsAny<string>())).Returns(Task.CompletedTask);
            var app = new ButtonApp(NullLogger.Instance, AppConfig.Empty().WithName(AppNames.ButtonApp), Address(), new Mock<IAwtrixService>().Object, mqtt.Object);
            await app.InitAsync();
            var clicks = 0;
            app.Click += (_, _) => clicks++;

            await app.DisposeAsync();
            mqtt.Raise(m => m.MessageReceived += null,
                new object[] { MqttTestHelpers.CreateReceivedArgs("awtrix/clock1/stats/buttonLeft", "1") });

            Assert.Equal(0, clicks);
            mqtt.VerifyRemove(m => m.MessageReceived -= It.IsAny<Func<MqttApplicationMessageReceivedEventArgs, Task>>(), Times.Once);
        }

        [Fact]
        public async Task TripTimerApp_Dispose_WhileActive_DoesNotBlockOnSlowAppClear()
        {
            // WS1 deferred (e): TripTimerApp.Dispose used to block on DeactivateAsync -> AppClear
            var awtrix = new Mock<IAwtrixService>();
            var clearIsSlow = false;
            var slowClear = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>()))
                .Returns(() => clearIsSlow ? slowClear.Task : Task.FromResult(true));
            var planner = new Mock<ITripPlannerService>();
            planner.Setup(p => p.GetNextDepartures(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new List<TripSummary>());
            var config = new TripTimerAppConfig
            {
                CronSchedule = "10 6 * * 1-5",
                ActiveTime = TimeSpan.FromMinutes(30),
                TimeToOrigin = TimeSpan.Zero,
                TimeToPrepare = TimeSpan.Zero,
            };
            config.Type = AppNames.TripTimerApp;
            var app = new TripTimerApp(NullLogger.Instance, new MockClock(Noon), Address(), awtrix.Object,
                new Mock<ITimerService>().Object, config, planner.Object);
            app.ExecuteNow();
            clearIsSlow = true;

            try
            {
                await Task.Run(() => app.Dispose()).WaitAsync(Guard);
            }
            finally
            {
                slowClear.TrySetResult(true);
            }
        }
    }
}
