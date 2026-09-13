using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Services.TripPlanner;
using Moq;
using Test.Apps;
using Test.Apps.MqttRender;

namespace Test.HostedServices
{
    /// <summary>
    /// Conductor.StartAsync over interface mocks: startup must never block or throw because of one app,
    /// the broker, or a device (CR-05, CR-06, CR-30).
    /// </summary>
    public class ConductorStartupTests
    {
        private static readonly DateTimeOffset HalfPastMidnight = new DateTimeOffset(2026, 9, 13, 0, 30, 0, TimeSpan.FromHours(10));

        private const string HttpDevice = "http://192.168.1.50/api";

        private static DeviceConfig Device(string baseTopic, params AppConfig[] apps)
        {
            var device = new DeviceConfig { BaseTopic = baseTopic };
            device.Apps.AddRange(apps);
            return device;
        }

        private static AppConfig App(string type) => AppConfig.Empty().WithName(type);

        private static AppConfig Diurnal()
        {
            var config = App(AppNames.DiurnalApp);
            config.Config["0600"] = "Brightness=8";
            return config;
        }

        private static AppConfig TripTimer()
        {
            var config = App(AppNames.TripTimerApp);
            config.Config["CronSchedule"] = "0 6 * * 1-5";
            config.Config["ActiveTime"] = "00:30:00";
            config.Config["TimeToOrigin"] = "00:10:00";
            config.Config["TimeToPrepare"] = "00:05:00";
            config.Config["StopIdOrigin"] = "1";
            config.Config["StopIdDestination"] = "2";
            return config;
        }

        private static void RaiseDoubleClick(Mock<IMqttConnector> mqtt, string topic)
        {
            foreach (var payload in new[] { "1", "0", "1" })
            {
                mqtt.Raise(m => m.MessageReceived += null, new object[] { MqttTestHelpers.CreateReceivedArgs(topic, payload) });
            }
        }

        [Fact]
        public async Task StartAsync_WhenMqttSubscribeNeverCompletes_StillCompletes()
        {
            var device = new DeviceConfig { BaseTopic = "awtrix/clock1" };
            var mqtt = new Mock<IMqttConnector>();
            mqtt.Setup(m => m.Subscribe(It.IsAny<string>())).Returns(new TaskCompletionSource().Task);
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                mqttConnector: mqtt.Object,
                clock: new MockClock(HalfPastMidnight));

            await Task.Run(() => conductor.StartAsync(CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Single(conductor.FindApps(AppNames.ButtonApp));
        }

        [Fact]
        public async Task StartAsync_TwoDevices_InitialisesEachAppExactlyOnce()
        {
            // CR-06: device 1's apps used to be re-initialised once per later device
            // WS5: Diurnal subscribes only when its schedule has a valid entry, so give each one
            var clock1 = Device("awtrix/clock1", Diurnal());
            var clock2 = Device("awtrix/clock2", Diurnal());
            var awtrix = new Mock<IAwtrixService>();
            var mqtt = new Mock<IMqttConnector>();
            var timer = new Mock<ITimerService>();
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { clock1, clock2 } },
                awtrixService: awtrix.Object,
                mqttConnector: mqtt.Object,
                clock: new MockClock(HalfPastMidnight),
                timerService: timer.Object);

            await conductor.StartAsync(CancellationToken.None);

            awtrix.Verify(a => a.AppClear(clock1, AppNames.DiurnalApp), Times.Once);
            awtrix.Verify(a => a.AppClear(clock1, AppNames.ButtonApp), Times.Once);
            awtrix.Verify(a => a.AppClear(clock2, AppNames.DiurnalApp), Times.Once);
            mqtt.Verify(m => m.Subscribe("awtrix/clock1/stats/buttonLeft"), Times.Once);
            timer.VerifyAdd(t => t.MinuteChanged += It.IsAny<EventHandler<ClockTickEventArgs>>(), Times.Exactly(2));
        }

        [Fact]
        public async Task StartAsync_CalledTwice_DoesNotCreateOrInitialiseAgain()
        {
            var device = Device(HttpDevice, App(AppNames.DiurnalApp));
            var awtrix = new Mock<IAwtrixService>();
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                awtrixService: awtrix.Object,
                clock: new MockClock(HalfPastMidnight));

            await conductor.StartAsync(CancellationToken.None);
            await conductor.StartAsync(CancellationToken.None);

            awtrix.Verify(a => a.AppClear(device, AppNames.DiurnalApp), Times.Once);
            Assert.Single(conductor.FindApps(AppNames.DiurnalApp));
        }

        [Fact]
        public async Task StartAsync_WhenOneAppInitThrows_StartsTheOthers()
        {
            // CR-05: one bad app must not kill every device
            var device = Device(HttpDevice, App(AppNames.DiurnalApp), App(AppNames.SlackStatusApp));
            var awtrix = new Mock<IAwtrixService>();
            awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), AppNames.DiurnalApp))
                .ThrowsAsync(new InvalidOperationException("boom"));
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                awtrixService: awtrix.Object,
                clock: new MockClock(HalfPastMidnight));

            var exception = await Record.ExceptionAsync(() => conductor.StartAsync(CancellationToken.None));

            Assert.Null(exception);
            Assert.Empty(conductor.FindApps(AppNames.DiurnalApp));
            Assert.Single(conductor.FindApps(AppNames.SlackStatusApp));
        }

        [Fact]
        public async Task StartAsync_UnknownAppType_IsSkippedAndOtherAppsStart()
        {
            // CR-30
            var device = Device(HttpDevice, App("MqttRendrApp"), App(AppNames.DiurnalApp));
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                clock: new MockClock(HalfPastMidnight));

            var exception = await Record.ExceptionAsync(() => conductor.StartAsync(CancellationToken.None));

            Assert.Null(exception);
            Assert.Empty(conductor.FindApps("MqttRendrApp"));
            Assert.Single(conductor.FindApps(AppNames.DiurnalApp));
        }

        [Fact]
        public async Task StartAsync_AppWithNoType_IsSkippedAndOtherAppsStart()
        {
            var device = Device(HttpDevice, new AppConfig(), App(AppNames.DiurnalApp));
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                clock: new MockClock(HalfPastMidnight));

            var exception = await Record.ExceptionAsync(() => conductor.StartAsync(CancellationToken.None));

            Assert.Null(exception);
            Assert.Single(conductor.FindApps(AppNames.DiurnalApp));
        }

        [Fact]
        public async Task StartAsync_ScheduledAppWithoutCronSchedule_IsRejectedAtCreation_AndNeverDisposed()
        {
            // CR-30: CrontabSchedule.Parse(null) used to fail host start
            // WS7 CR-23: config validation now rejects the app before construction, so there is nothing to dispose.
            var device = Device(HttpDevice, App(AppNames.MqttRenderApp), App(AppNames.DiurnalApp));
            var awtrix = new Mock<IAwtrixService>();
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                awtrixService: awtrix.Object,
                clock: new MockClock(HalfPastMidnight));

            var exception = await Record.ExceptionAsync(() => conductor.StartAsync(CancellationToken.None));

            Assert.Null(exception);
            Assert.Empty(conductor.FindApps(AppNames.MqttRenderApp));
            Assert.Single(conductor.FindApps(AppNames.DiurnalApp));
            // Never constructed: no InitAsync clear and no dispose clear.
            awtrix.Verify(a => a.AppClear(device, AppNames.MqttRenderApp), Times.Never);
            awtrix.Verify(a => a.Dismiss(It.IsAny<AwtrixAddress>()), Times.Never);
        }

        [Fact]
        public async Task StartAsync_RightDoubleClick_StartsOnlyThatDevicesTripTimerOnce()
        {
            var clock1 = Device("awtrix/clock1", TripTimer());
            var clock2 = Device("awtrix/clock2", TripTimer());
            var awtrix = new Mock<IAwtrixService>();
            var mqtt = new Mock<IMqttConnector>();
            var tripPlanner = new Mock<ITripPlannerService>();
            tripPlanner
                .Setup(t => t.GetNextDepartures(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TripSummary>());
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { clock1, clock2 } },
                awtrixService: awtrix.Object,
                mqttConnector: mqtt.Object,
                clock: new MockClock(HalfPastMidnight),
                tripPlanner: tripPlanner.Object);
            await conductor.StartAsync(CancellationToken.None);

            RaiseDoubleClick(mqtt, "awtrix/clock1/stats/buttonRight");

            // TripTimerApp's activation announces itself with one Notify ("Starting trip timer")
            awtrix.Verify(a => a.Notify(clock1, It.IsAny<AwtrixAppMessage>()), Times.Once);
            awtrix.Verify(a => a.Notify(clock2, It.IsAny<AwtrixAppMessage>()), Times.Never);

            await conductor.StopAsync(CancellationToken.None);
        }
    }
}
