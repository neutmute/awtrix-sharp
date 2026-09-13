using System.Reflection;
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.Diurnal;
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Apps.SlackStatus;
using AwtrixSharpWeb.Apps.TripTimer;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Test.Apps;
using Test.Services;

namespace Test.HostedServices
{
    /// <summary>
    /// Covers Conductor's app factory switch, the registry (FindApps) and the StartAsync basics.
    /// Startup isolation: ConductorStartupTests. ExecuteNow: ConductorExecuteNowTests. Stop: ConductorShutdownTests.
    /// </summary>
    public class ConductorTests
    {
        private static IAwtrixApp? InvokeAppFactory(Conductor conductor, DeviceConfig device, AppConfig appConfig)
        {
            var method = typeof(Conductor).GetMethod("AppFactory", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            try
            {
                return (IAwtrixApp?)method!.Invoke(conductor, new object[] { device, appConfig });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        private static DeviceConfig CreateDevice() => new DeviceConfig { BaseTopic = "awtrix/clock1" };

        /// <summary>
        /// WS7 CR-23: scheduled app types are validated at creation, so the factory needs a valid config.
        /// </summary>
        private static AppConfig ValidScheduledConfig(string type)
        {
            var config = AppConfig.Empty().WithName(type);
            config.Config["CronSchedule"] = "0 8 * * *";
            config.Config["ActiveTime"] = "00:30:00";
            config.Config["ReadTopic"] = "openhab/temperature/room-j";
            config.Config["StopIdOrigin"] = "200060";
            config.Config["StopIdDestination"] = "200070";
            return config;
        }

        private static readonly DateTimeOffset HalfPastMidnight = new DateTimeOffset(2026, 9, 13, 0, 30, 0, TimeSpan.FromHours(10));

        [Fact]
        public void AppFactory_DiurnalApp_CreatesDiurnalApp()
        {
            var app = InvokeAppFactory(ConductorTestHelper.Create(), CreateDevice(), AppConfig.Empty().WithName(AppNames.DiurnalApp));

            Assert.IsType<DiurnalApp>(app);
        }

        [Fact]
        public void AppFactory_ButtonApp_CreatesButtonApp()
        {
            var app = InvokeAppFactory(ConductorTestHelper.Create(), CreateDevice(), AppConfig.Empty().WithName(AppNames.ButtonApp));

            Assert.IsType<ButtonApp>(app);
        }

        [Fact]
        public void AppFactory_MqttRenderApp_CreatesMqttRenderApp()
        {
            var app = InvokeAppFactory(ConductorTestHelper.Create(), CreateDevice(), ValidScheduledConfig(AppNames.MqttRenderApp));

            Assert.IsType<MqttRenderApp>(app);
        }

        [Fact]
        public void AppFactory_MqttClockRenderApp_CreatesMqttClockRenderApp()
        {
            var app = InvokeAppFactory(ConductorTestHelper.Create(), CreateDevice(), ValidScheduledConfig(AppNames.MqttClockRenderApp));

            Assert.IsType<MqttClockRenderApp>(app);
        }

        [Fact]
        public void AppFactory_TripTimerApp_CreatesTripTimerApp()
        {
            var app = InvokeAppFactory(ConductorTestHelper.Create(), CreateDevice(), ValidScheduledConfig(AppNames.TripTimerApp));

            Assert.IsType<TripTimerApp>(app);
        }

        [Fact]
        public void AppFactory_SlackStatusApp_CreatesSlackStatusApp()
        {
            var app = InvokeAppFactory(ConductorTestHelper.Create(), CreateDevice(), AppConfig.Empty().WithName(AppNames.SlackStatusApp));

            Assert.IsType<SlackStatusApp>(app);
        }

        [Fact]
        public void AppFactory_UnknownType_ReturnsNullWithoutThrowing()
        {
            // CR-30: a typo in Type is logged and skipped, not fatal
            var app = InvokeAppFactory(ConductorTestHelper.Create(), CreateDevice(), AppConfig.Empty().WithName("SomeUnknownAppType"));

            Assert.Null(app);
        }

        [Fact]
        public void AppFactory_CreatedApp_HasExpectedAwtrixAddress()
        {
            var app = InvokeAppFactory(ConductorTestHelper.Create(), CreateDevice(), AppConfig.Empty().WithName(AppNames.DiurnalApp));

            Assert.Equal("awtrix/clock1", app!.AwtrixAddress.BaseTopic);
        }

        [Fact]
        public void AppNamesAll_ListsEveryFactoryType()
        {
            Assert.Equal(
                new[] { AppNames.DiurnalApp, AppNames.ButtonApp, AppNames.TripTimerApp, AppNames.SlackStatusApp, AppNames.MqttRenderApp, AppNames.MqttClockRenderApp },
                AppNames.All);
        }

        [Fact]
        public void AppNamesConfigurable_ListsConfigTypes_WithoutAutoCreatedButtonApp()
        {
            Assert.Equal(
                new[] { AppNames.DiurnalApp, AppNames.TripTimerApp, AppNames.SlackStatusApp, AppNames.MqttRenderApp, AppNames.MqttClockRenderApp },
                AppNames.Configurable);
        }

        [Fact]
        public void FindApps_WhenNoAppsRegistered_ReturnsEmptyList()
        {
            var conductor = ConductorTestHelper.Create();

            Assert.Empty(conductor.FindApps(AppNames.DiurnalApp));
        }

        [Fact]
        public void FindApps_FiltersByConfigType()
        {
            var conductor = ConductorTestHelper.Create();
            var matching = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.DiurnalApp);
            var nonMatching = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.MqttRenderApp);
            conductor.RegisterApp(matching.Object);
            conductor.RegisterApp(nonMatching.Object);

            var result = conductor.FindApps(AppNames.DiurnalApp);

            Assert.Single(result);
            Assert.Same(matching.Object, result[0]);
        }

        [Fact]
        public void FindApps_WithBaseTopic_ReturnsOnlyThatDevicesApps()
        {
            var conductor = ConductorTestHelper.Create();
            var clock1 = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.TripTimerApp);
            var clock2 = ConductorTestHelper.MockApp("awtrix/clock2", AppNames.TripTimerApp);
            conductor.RegisterApp(clock1.Object);
            conductor.RegisterApp(clock2.Object);

            Assert.Same(clock2.Object, Assert.Single(conductor.FindApps(AppNames.TripTimerApp, "awtrix/clock2")));
            Assert.Equal(2, conductor.FindApps(AppNames.TripTimerApp).Count);
            Assert.Empty(conductor.FindApps(AppNames.TripTimerApp, "AWTRIX/CLOCK2"));
        }

        [Fact]
        public async Task StartAsync_HttpDevice_DoesNotCreateButtonApp()
        {
            var device = new DeviceConfig { BaseTopic = "http://192.168.1.50/api" };
            device.Apps.Add(AppConfig.Empty().WithName(AppNames.DiurnalApp));
            var awtrix = new Mock<IAwtrixService>();
            var mqtt = new Mock<IMqttConnector>();
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                awtrixService: awtrix.Object,
                mqttConnector: mqtt.Object,
                clock: new MockClock(HalfPastMidnight));

            await conductor.StartAsync(CancellationToken.None);

            Assert.Empty(conductor.FindApps(AppNames.ButtonApp));
            Assert.Single(conductor.FindApps(AppNames.DiurnalApp));
            mqtt.Verify(m => m.Subscribe(It.IsAny<string>()), Times.Never);
            awtrix.Verify(a => a.AppClear(device, AppNames.DiurnalApp), Times.Once);
        }

        [Fact]
        public async Task StartAsync_MqttDevice_CreatesButtonAppAndSubscribesToButtons()
        {
            var device = new DeviceConfig { BaseTopic = "awtrix/clock1" };
            var mqtt = new Mock<IMqttConnector>();
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                mqttConnector: mqtt.Object,
                clock: new MockClock(HalfPastMidnight));

            await conductor.StartAsync(CancellationToken.None);

            Assert.Single(conductor.FindApps(AppNames.ButtonApp, "awtrix/clock1"));
            mqtt.Verify(m => m.Subscribe("awtrix/clock1/stats/buttonLeft"), Times.Once);
            mqtt.Verify(m => m.Subscribe("awtrix/clock1/stats/buttonSelect"), Times.Once);
            mqtt.Verify(m => m.Subscribe("awtrix/clock1/stats/buttonRight"), Times.Once);
        }

        [Fact]
        public async Task StartAsync_WithUnreachableHttpDevice_Completes()
        {
            // CR-02 scenario: device unplugged at startup must not fail host start
            var offline = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("No route to host"));
            var httpPublisher = new HttpPublisher(NullLogger<HttpPublisher>.Instance, new StubHttpClientFactory(offline));
            var awtrixService = new AwtrixService(httpPublisher, new FakeMqttPublisher());

            var device = new DeviceConfig { BaseTopic = "http://192.168.1.50/api" };
            var diurnal = AppConfig.Empty().WithName(AppNames.DiurnalApp);
            diurnal.Config["0000"] = "Brightness=8"; // before "now", so startup replay publishes too
            device.Apps.Add(diurnal);

            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                awtrixService: awtrixService,
                clock: new MockClock(HalfPastMidnight));

            var exception = await Record.ExceptionAsync(() => conductor.StartAsync(CancellationToken.None));

            Assert.Null(exception);
            Assert.Single(conductor.FindApps(AppNames.DiurnalApp));
            Assert.NotEmpty(offline.Requests);
        }
    }
}
