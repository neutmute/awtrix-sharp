using AwtrixSharpWeb.HostedServices;
using Moq;

namespace Test.HostedServices
{
    /// <summary>
    /// Conductor.StopAsync awaits each app's DisposeAsync in isolation, bounded by a per-app timeout
    /// and the host shutdown token, and empties the registry first.
    /// </summary>
    public class ConductorShutdownTests
    {
        [Fact]
        public async Task StopAsync_WithNoRegisteredApps_CompletesWithoutError()
        {
            var conductor = ConductorTestHelper.Create();

            var exception = await Record.ExceptionAsync(() => conductor.StopAsync(CancellationToken.None));

            Assert.Null(exception);
        }

        [Fact]
        public async Task StopAsync_DisposesEveryRegisteredAppAsynchronously()
        {
            var conductor = ConductorTestHelper.Create();
            var app1 = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.DiurnalApp);
            var app2 = ConductorTestHelper.MockApp("awtrix/clock2", AppNames.DiurnalApp);
            conductor.RegisterApp(app1.Object);
            conductor.RegisterApp(app2.Object);

            await conductor.StopAsync(CancellationToken.None);

            app1.Verify(a => a.DisposeAsync(), Times.Once);
            app2.Verify(a => a.DisposeAsync(), Times.Once);
            app1.Verify(a => a.Dispose(), Times.Never);
            app2.Verify(a => a.Dispose(), Times.Never);
        }

        [Fact]
        public async Task StopAsync_AwaitsPendingAsyncDisposal()
        {
            var conductor = ConductorTestHelper.Create();
            var gate = new TaskCompletionSource();
            var app = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.MqttRenderApp);
            app.Setup(a => a.DisposeAsync()).Returns(new ValueTask(gate.Task));
            conductor.RegisterApp(app.Object);

            var stop = conductor.StopAsync(CancellationToken.None);

            Assert.False(stop.IsCompleted);
            gate.SetResult();
            await stop.WaitAsync(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task StopAsync_WhenOneAppDisposeThrows_StillDisposesOthers()
        {
            var conductor = ConductorTestHelper.Create();
            var throwing = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.DiurnalApp);
            throwing.Setup(a => a.DisposeAsync()).Throws(new InvalidOperationException("offline"));
            var faulting = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.MqttRenderApp);
            faulting.Setup(a => a.DisposeAsync()).Returns(new ValueTask(Task.FromException(new HttpRequestException("offline"))));
            var healthy = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.SlackStatusApp);
            conductor.RegisterApp(throwing.Object);
            conductor.RegisterApp(faulting.Object);
            conductor.RegisterApp(healthy.Object);

            var exception = await Record.ExceptionAsync(() => conductor.StopAsync(CancellationToken.None));

            Assert.Null(exception);
            healthy.Verify(a => a.DisposeAsync(), Times.Once);
        }

        [Fact]
        public async Task StopAsync_WhenShutdownTokenIsCancelled_DoesNotWaitForAHungApp()
        {
            var conductor = ConductorTestHelper.Create();
            var hung = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.TripTimerApp);
            hung.Setup(a => a.DisposeAsync()).Returns(new ValueTask(new TaskCompletionSource().Task));
            var healthy = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.DiurnalApp);
            conductor.RegisterApp(hung.Object);
            conductor.RegisterApp(healthy.Object);
            using var shutdown = new CancellationTokenSource();
            shutdown.Cancel();

            await conductor.StopAsync(shutdown.Token).WaitAsync(TimeSpan.FromSeconds(5));

            hung.Verify(a => a.DisposeAsync(), Times.Once);
            healthy.Verify(a => a.DisposeAsync(), Times.Once);
        }

        [Fact]
        public async Task StopAsync_ClearsRegistry_SoExecuteNowReturnsNotFound()
        {
            var conductor = ConductorTestHelper.Create();
            var app = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.TripTimerApp);
            conductor.RegisterApp(app.Object);

            await conductor.StopAsync(CancellationToken.None);

            Assert.Empty(conductor.FindApps(AppNames.TripTimerApp));
            Assert.Equal(AppExecutionResult.NotFound, conductor.ExecuteNow("awtrix/clock1", AppNames.TripTimerApp));
            app.Verify(a => a.ExecuteNow(), Times.Never);
        }

        [Fact]
        public void AppDisposeTimeout_CoversTwoHttpPublishTimeouts()
        {
            Assert.Equal(TimeSpan.FromSeconds(10), Conductor.AppDisposeTimeout);
        }
    }
}
