using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Test.HostedServices
{
    /// <summary>
    /// Builds a Conductor over interface mocks. Moq's defaults return completed tasks
    /// (Task&lt;bool&gt; => false), so StartAsync can run without a broker or network.
    /// </summary>
    internal static class ConductorTestHelper
    {
        public static Conductor Create(
            AwtrixConfig? config = null,
            IHostEnvironment? hostEnvironment = null,
            IAwtrixService? awtrixService = null,
            IMqttConnector? mqttConnector = null,
            IClock? clock = null)
        {
            config ??= new AwtrixConfig { Devices = Array.Empty<DeviceConfig>() };

            var env = hostEnvironment;
            if (env == null)
            {
                var envMock = new Mock<IHostEnvironment>();
                envMock.Setup(e => e.EnvironmentName).Returns("Production");
                env = envMock.Object;
            }

            return new Conductor(
                NullLogger<Conductor>.Instance,
                env,
                Options.Create(config),
                new Mock<ITimerService>().Object,
                new Mock<ITripPlannerService>().Object,
                awtrixService ?? new Mock<IAwtrixService>().Object,
                new Mock<ISlackConnector>().Object,
                mqttConnector ?? new Mock<IMqttConnector>().Object,
                clock ?? new Clock(),
                NullLoggerFactory.Instance);
        }
    }
}
