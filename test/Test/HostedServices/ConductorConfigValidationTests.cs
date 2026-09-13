using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Moq;

namespace Test.HostedServices
{
    /// <summary>
    /// CR-23: invalid typed config is rejected when the app is created, inside Conductor's per-app guard (WS3),
    /// so the app never starts and the other apps on the device still run.
    /// </summary>
    public class ConductorConfigValidationTests
    {
        private const string Device = "awtrix/clock1";

        private static AppConfig App(string type, Dictionary<string, string> keys)
        {
            var app = new AppConfig { Type = type };
            foreach (var kvp in keys)
            {
                app.Config.Add(kvp.Key, kvp.Value);
            }
            return app;
        }

        private static AppConfig Diurnal() => App(AppNames.DiurnalApp, new() { ["0600"] = "Brightness=8" });

        private static Dictionary<string, string> TripTimerKeys() => new()
        {
            ["CronSchedule"] = "10 6 * * 1-5",
            ["ActiveTime"] = "01:00:00",
            ["StopIdOrigin"] = "200060",
            ["StopIdDestination"] = "200070",
            ["TimeToOrigin"] = "00:14:00",
            ["TimeToPrepare"] = "00:08:00",
        };

        private static Dictionary<string, string> MqttKeys(string readTopic) => new()
        {
            ["CronSchedule"] = "0 8 * * *",
            ["ActiveTime"] = "09:00:00",
            ["ReadTopic"] = readTopic,
        };

        private static AwtrixConfig ConfigWith(params AppConfig[] apps) => new()
        {
            Devices = new[] { new DeviceConfig { BaseTopic = Device, Apps = apps.ToList() } },
        };

        [Fact]
        public async Task TripTimerApp_MissingStopIdOrigin_IsSkipped_OtherAppsRun()
        {
            var keys = TripTimerKeys();
            keys.Remove("StopIdOrigin");
            var awtrixService = new Mock<IAwtrixService>();
            var conductor = ConductorTestHelper.Create(ConfigWith(App(AppNames.TripTimerApp, keys), Diurnal()), awtrixService: awtrixService.Object);

            await conductor.StartAsync(CancellationToken.None);
            try
            {
                Assert.Empty(conductor.FindApps(AppNames.TripTimerApp));
                Assert.Single(conductor.FindApps(AppNames.DiurnalApp));
                awtrixService.Verify(s => s.AppClear(It.IsAny<AwtrixAddress>(), AppNames.TripTimerApp), Times.Never);
            }
            finally
            {
                await conductor.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task MqttRenderApp_InvalidActiveTime_IsSkipped_OtherAppsRun()
        {
            var keys = MqttKeys("openhab/fronius/grid-surplus");
            keys["ActiveTime"] = "abc";
            var awtrixService = new Mock<IAwtrixService>();
            var conductor = ConductorTestHelper.Create(ConfigWith(App(AppNames.MqttRenderApp, keys), Diurnal()), awtrixService: awtrixService.Object);

            await conductor.StartAsync(CancellationToken.None);
            try
            {
                Assert.Empty(conductor.FindApps(AppNames.MqttRenderApp));
                Assert.Single(conductor.FindApps(AppNames.DiurnalApp));
                awtrixService.Verify(s => s.AppClear(It.IsAny<AwtrixAddress>(), AppNames.MqttRenderApp), Times.Never);
            }
            finally
            {
                await conductor.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task ValidScheduledApps_AreRegistered()
        {
            var conductor = ConductorTestHelper.Create(ConfigWith(
                App(AppNames.TripTimerApp, TripTimerKeys()),
                App(AppNames.MqttRenderApp, MqttKeys("openhab/fronius/grid-surplus")),
                App(AppNames.MqttClockRenderApp, MqttKeys("openhab/temperature/room-j"))));

            await conductor.StartAsync(CancellationToken.None);
            try
            {
                Assert.Single(conductor.FindApps(AppNames.TripTimerApp));
                Assert.Single(conductor.FindApps(AppNames.MqttRenderApp));
                Assert.Single(conductor.FindApps(AppNames.MqttClockRenderApp));
            }
            finally
            {
                await conductor.StopAsync(CancellationToken.None);
            }
        }
    }
}
