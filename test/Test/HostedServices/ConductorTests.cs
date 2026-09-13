using System.Reflection;
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.Diurnal;
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Apps.SlackStatus;
using AwtrixSharpWeb.Apps.TripTimer;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using Moq;

namespace Test.HostedServices
{
    /// <summary>
    /// Covers Conductor's app factory switch, ExecuteNow's guard clauses, and FindApps
    /// filtering. Conductor.StartAsync() itself is NOT exercised here because it calls
    /// IAwtrixApp.Init(), which synchronously blocks on a real MQTT/HTTP publish attempt
    /// for every app (Conductor hard-codes `new AwtrixService(_httpPublisher, _mqttPublisher)`
    /// with no injection seam) - see the final report's testability blockers.
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

        private static void SetAppsList(AwtrixSharpWeb.HostedServices.Conductor conductor, List<IAwtrixApp> apps)
        {
            var field = typeof(AwtrixSharpWeb.HostedServices.Conductor).GetField("_apps", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            field!.SetValue(conductor, apps);
        }
    }
}
