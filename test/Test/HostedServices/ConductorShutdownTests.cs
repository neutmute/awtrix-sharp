using AwtrixSharpWeb.HostedServices;
using Moq;

namespace Test.HostedServices
{
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
        public async Task StopAsync_DisposesAllRegisteredApps()
        {
            var conductor = ConductorTestHelper.Create();
            var app1 = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.DiurnalApp);
            var app2 = ConductorTestHelper.MockApp("awtrix/clock2", AppNames.DiurnalApp);
            conductor.RegisterApp(app1.Object);
            conductor.RegisterApp(app2.Object);

            await conductor.StopAsync(CancellationToken.None);

            app1.Verify(a => a.Dispose(), Times.Once);
            app2.Verify(a => a.Dispose(), Times.Once);
        }

        [Fact]
        public async Task StopAsync_WhenOneAppDisposeThrows_StillDisposesOthers()
        {
            var conductor = ConductorTestHelper.Create();
            var throwing = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.DiurnalApp);
            throwing.Setup(a => a.Dispose()).Throws(new AggregateException(new HttpRequestException("offline")));
            var healthy = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.SlackStatusApp);
            conductor.RegisterApp(throwing.Object);
            conductor.RegisterApp(healthy.Object);

            var exception = await Record.ExceptionAsync(() => conductor.StopAsync(CancellationToken.None));

            Assert.Null(exception);
            healthy.Verify(a => a.Dispose(), Times.Once);
        }

        [Fact]
        public async Task StopAsync_ClearsTheRegistry()
        {
            var conductor = ConductorTestHelper.Create();
            conductor.RegisterApp(ConductorTestHelper.MockApp("awtrix/clock1", AppNames.DiurnalApp).Object);

            await conductor.StopAsync(CancellationToken.None);

            Assert.Empty(conductor.FindApps(AppNames.DiurnalApp));
        }
    }
}
