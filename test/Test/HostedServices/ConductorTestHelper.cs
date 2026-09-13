using AwtrixSharpWeb.Apps.Configs;
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
            IClock? clock = null,
            ITimerService? timerService = null,
            ITripPlannerService? tripPlanner = null,
            ISlackConnector? slackConnector = null,
            SlackSettings? slackSettings = null)
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
                timerService ?? new Mock<ITimerService>().Object,
                tripPlanner ?? new Mock<ITripPlannerService>().Object,
                awtrixService ?? new Mock<IAwtrixService>().Object,
                slackConnector ?? new Mock<ISlackConnector>().Object,
                mqttConnector ?? new Mock<IMqttConnector>().Object,
                clock ?? new Clock(),
                NullLoggerFactory.Instance,
                slackSettings: slackSettings is null ? null : Options.Create(slackSettings));
        }

        /// <summary>
        /// An app mock that reports the device and Type the Conductor registry keys on.
        /// </summary>
        public static Mock<IAwtrixApp> MockApp(string baseTopic, string type)
        {
            var app = new Mock<IAwtrixApp>();
            app.Setup(a => a.AwtrixAddress).Returns(new AwtrixAddress { BaseTopic = baseTopic });
            app.Setup(a => a.GetConfig()).Returns(AppConfig.Empty().WithName(type));
            return app;
        }
    }
}
