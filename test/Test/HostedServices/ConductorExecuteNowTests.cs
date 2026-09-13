using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;
using Moq;
using Test.Apps;

namespace Test.HostedServices
{
    /// <summary>
    /// CR-07: ExecuteNow drives the registered instance and never builds a transient, cron-armed duplicate.
    /// </summary>
    public class ConductorExecuteNowTests
    {
        [Fact]
        public void ExecuteNow_RunningApp_ExecutesThatInstanceAndReturnsStarted()
        {
            var conductor = ConductorTestHelper.Create();
            var app = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.TripTimerApp);
            conductor.RegisterApp(app.Object);

            var first = conductor.ExecuteNow("awtrix/clock1", AppNames.TripTimerApp);
            var second = conductor.ExecuteNow("awtrix/clock1", AppNames.TripTimerApp);

            Assert.Equal(AppExecutionResult.Started, first);
            Assert.Equal(AppExecutionResult.Started, second);
            app.Verify(a => a.ExecuteNow(), Times.Exactly(2));
            app.Verify(a => a.InitAsync(), Times.Never);
            Assert.Single(conductor.FindApps(AppNames.TripTimerApp));
        }

        [Fact]
        public async Task ExecuteNow_AfterStartAsync_DoesNotInitialiseAnotherInstance()
        {
            var device = new DeviceConfig { BaseTopic = "http://192.168.1.50/api" };
            device.Apps.Add(AppConfig.Empty().WithName(AppNames.DiurnalApp));
            var awtrix = new Mock<IAwtrixService>();
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                awtrixService: awtrix.Object,
                clock: new MockClock(new DateTimeOffset(2026, 9, 13, 0, 30, 0, TimeSpan.FromHours(10))));
            await conductor.StartAsync(CancellationToken.None);

            var result = conductor.ExecuteNow(device.BaseTopic, AppNames.DiurnalApp);

            Assert.Equal(AppExecutionResult.Started, result);
            awtrix.Verify(a => a.AppClear(device, AppNames.DiurnalApp), Times.Once);
        }

        [Fact]
        public void ExecuteNow_TargetsOnlyTheRequestedDevice()
        {
            var conductor = ConductorTestHelper.Create();
            var clock1 = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.TripTimerApp);
            var clock2 = ConductorTestHelper.MockApp("awtrix/clock2", AppNames.TripTimerApp);
            conductor.RegisterApp(clock1.Object);
            conductor.RegisterApp(clock2.Object);

            conductor.ExecuteNow("awtrix/clock2", AppNames.TripTimerApp);

            clock1.Verify(a => a.ExecuteNow(), Times.Never);
            clock2.Verify(a => a.ExecuteNow(), Times.Once);
        }

        [Theory]
        [InlineData("awtrix/does-not-exist", AppNames.TripTimerApp)]
        [InlineData("awtrix/clock1", "NoSuchApp")]
        [InlineData("", AppNames.TripTimerApp)]
        [InlineData("awtrix/clock1", "")]
        public void ExecuteNow_NoMatchingRunningApp_ReturnsNotFound(string baseTopic, string appType)
        {
            var conductor = ConductorTestHelper.Create();
            var app = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.TripTimerApp);
            conductor.RegisterApp(app.Object);

            var result = conductor.ExecuteNow(baseTopic, appType);

            Assert.Equal(AppExecutionResult.NotFound, result);
            app.Verify(a => a.ExecuteNow(), Times.Never);
        }

        [Fact]
        public void ExecuteNow_WhenAppThrows_ReturnsError()
        {
            var conductor = ConductorTestHelper.Create();
            var app = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.MqttRenderApp);
            app.Setup(a => a.ExecuteNow()).Throws(new ObjectDisposedException("cts"));
            conductor.RegisterApp(app.Object);

            var result = conductor.ExecuteNow("awtrix/clock1", AppNames.MqttRenderApp);

            Assert.Equal(AppExecutionResult.Error, result);
        }

        [Fact]
        public void ExecuteNow_DuplicateEntries_ExecutesBothAndReportsErrorIfEitherThrows()
        {
            var conductor = ConductorTestHelper.Create();
            var throwing = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.MqttRenderApp);
            throwing.Setup(a => a.ExecuteNow()).Throws(new InvalidOperationException("boom"));
            var healthy = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.MqttRenderApp);
            conductor.RegisterApp(throwing.Object);
            conductor.RegisterApp(healthy.Object);

            var result = conductor.ExecuteNow("awtrix/clock1", AppNames.MqttRenderApp);

            Assert.Equal(AppExecutionResult.Error, result);
            healthy.Verify(a => a.ExecuteNow(), Times.Once);
        }
    }
}
