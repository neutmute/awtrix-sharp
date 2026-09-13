using System.Net;
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
    /// Covers Conductor's app factory switch, ExecuteNow's guard clauses, FindApps filtering,
    /// and StartAsync/StopAsync. Conductor depends only on interfaces, so StartAsync runs over
    /// mocks (ConductorTestHelper) without a broker or network.
    /// </summary>
    public class ConductorTests
    {
        private static IAwtrixApp InvokeAppFactory(AwtrixSharpWeb.HostedServices.Conductor conductor, DeviceConfig device, AppConfig appConfig)
        {
            var method = typeof(AwtrixSharpWeb.HostedServices.Conductor).GetMethod("AppFactory", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            try
            {
                return (IAwtrixApp)method!.Invoke(conductor, new object[] { device, appConfig })!;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        private static DeviceConfig CreateDevice() => new DeviceConfig { BaseTopic = "awtrix/clock1" };

        [Fact]
        public void AppFactory_DiurnalApp_CreatesDiurnalApp()
        {
            var conductor = ConductorTestHelper.Create();
            var config = AppConfig.Empty().WithName(AppNames.DiurnalApp);

            var app = InvokeAppFactory(conductor, CreateDevice(), config);

            Assert.IsType<DiurnalApp>(app);
        }

        [Fact]
        public void AppFactory_ButtonApp_CreatesButtonApp()
        {
            var conductor = ConductorTestHelper.Create();
            var config = AppConfig.Empty().WithName(AppNames.ButtonApp);

            var app = InvokeAppFactory(conductor, CreateDevice(), config);

            Assert.IsType<ButtonApp>(app);
        }

        [Fact]
        public void AppFactory_MqttRenderApp_CreatesMqttRenderApp()
        {
            var conductor = ConductorTestHelper.Create();
            var config = AppConfig.Empty().WithName(AppNames.MqttRenderApp);

            var app = InvokeAppFactory(conductor, CreateDevice(), config);

            Assert.IsType<MqttRenderApp>(app);
        }

        [Fact]
        public void AppFactory_MqttClockRenderApp_CreatesMqttClockRenderApp()
        {
            var conductor = ConductorTestHelper.Create();
            var config = AppConfig.Empty().WithName(AppNames.MqttClockRenderApp);

            var app = InvokeAppFactory(conductor, CreateDevice(), config);

            Assert.IsType<MqttClockRenderApp>(app);
        }

        [Fact]
        public void AppFactory_TripTimerApp_CreatesTripTimerApp()
        {
            var conductor = ConductorTestHelper.Create();
            var config = AppConfig.Empty().WithName(AppNames.TripTimerApp);

            var app = InvokeAppFactory(conductor, CreateDevice(), config);

            Assert.IsType<TripTimerApp>(app);
        }

        [Fact]
        public void AppFactory_SlackStatusApp_CreatesSlackStatusApp()
        {
            var conductor = ConductorTestHelper.Create();
            var config = AppConfig.Empty().WithName(AppNames.SlackStatusApp);

            var app = InvokeAppFactory(conductor, CreateDevice(), config);

            Assert.IsType<SlackStatusApp>(app);
        }

        [Fact]
        public void AppFactory_UnknownType_ThrowsNotImplementedException()
        {
            var conductor = ConductorTestHelper.Create();
            var config = AppConfig.Empty().WithName("SomeUnknownAppType");

            Assert.Throws<NotImplementedException>(() => InvokeAppFactory(conductor, CreateDevice(), config));
        }

        [Fact]
        public void AppFactory_CreatedApp_HasExpectedAwtrixAddress()
        {
            var conductor = ConductorTestHelper.Create();
            var device = CreateDevice();
            var config = AppConfig.Empty().WithName(AppNames.DiurnalApp);

            var app = InvokeAppFactory(conductor, device, config);

            Assert.Equal("awtrix/clock1", app.AwtrixAddress.BaseTopic);
        }

        [Fact]
        public void ExecuteNow_UnknownDevice_DoesNotThrow()
        {
            var config = new AwtrixConfig
            {
                Devices = new[] { CreateDevice() }
            };
            var conductor = ConductorTestHelper.Create(config);

            var exception = Record.Exception(() => conductor.ExecuteNow("awtrix/does-not-exist", AppNames.DiurnalApp));

            Assert.Null(exception);
        }

        [Fact]
        public void ExecuteNow_UnknownAppOnKnownDevice_DoesNotThrow()
        {
            var device = CreateDevice();
            var config = new AwtrixConfig { Devices = new[] { device } };
            var conductor = ConductorTestHelper.Create(config);

            var exception = Record.Exception(() => conductor.ExecuteNow(device.BaseTopic, "NoSuchApp"));

            Assert.Null(exception);
        }

        [Fact]
        public void FindApps_WhenNoAppsRegistered_ReturnsEmptyList()
        {
            var conductor = ConductorTestHelper.Create();

            var result = conductor.FindApps(AppNames.DiurnalApp);

            Assert.Empty(result);
        }

        [Fact]
        public void FindApps_FiltersByConfigType()
        {
            var conductor = ConductorTestHelper.Create();
            var matching = new Mock<IAwtrixApp>();
            matching.Setup(a => a.GetConfig()).Returns(AppConfig.Empty().WithName(AppNames.DiurnalApp));
            var nonMatching = new Mock<IAwtrixApp>();
            nonMatching.Setup(a => a.GetConfig()).Returns(AppConfig.Empty().WithName(AppNames.MqttRenderApp));

            SetAppsList(conductor, new List<IAwtrixApp> { matching.Object, nonMatching.Object });

            var result = conductor.FindApps(AppNames.DiurnalApp);

            Assert.Single(result);
            Assert.Same(matching.Object, result[0]);
        }

        [Fact]
        public void StopAsync_WithNoRegisteredApps_CompletesWithoutError()
        {
            var conductor = ConductorTestHelper.Create();

            var task = conductor.StopAsync(CancellationToken.None);

            Assert.True(task.IsCompletedSuccessfully);
        }

        [Fact]
        public async Task StopAsync_DisposesAllRegisteredApps()
        {
            var conductor = ConductorTestHelper.Create();
            var app1 = new Mock<IAwtrixApp>();
            var app2 = new Mock<IAwtrixApp>();
            SetAppsList(conductor, new List<IAwtrixApp> { app1.Object, app2.Object });

            await conductor.StopAsync(CancellationToken.None);

            app1.Verify(a => a.Dispose(), Times.Once);
            app2.Verify(a => a.Dispose(), Times.Once);
        }

        private static readonly DateTimeOffset HalfPastMidnight = new DateTimeOffset(2026, 9, 13, 0, 30, 0, TimeSpan.FromHours(10));

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

            Assert.Single(conductor.FindApps(AppNames.ButtonApp));
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

        [Fact]
        public async Task StopAsync_WhenOneAppDisposeThrows_StillDisposesOthers()
        {
            var conductor = ConductorTestHelper.Create();
            var throwing = new Mock<IAwtrixApp>();
            throwing.Setup(a => a.Dispose()).Throws(new AggregateException(new HttpRequestException("offline")));
            var healthy = new Mock<IAwtrixApp>();
            SetAppsList(conductor, new List<IAwtrixApp> { throwing.Object, healthy.Object });

            var exception = await Record.ExceptionAsync(() => conductor.StopAsync(CancellationToken.None));

            Assert.Null(exception);
            healthy.Verify(a => a.Dispose(), Times.Once);
        }

        private static void SetAppsList(AwtrixSharpWeb.HostedServices.Conductor conductor, List<IAwtrixApp> apps)
        {
            var field = typeof(AwtrixSharpWeb.HostedServices.Conductor).GetField("_apps", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            field!.SetValue(conductor, apps);
        }
    }
}
