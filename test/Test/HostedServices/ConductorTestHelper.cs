using System.Net;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Test.Services;
using TransportOpenData.TripPlanner;

namespace Test.HostedServices
{
    /// <summary>
    /// Builds a fully real (non-mocked) Conductor instance for tests. Conductor's
    /// constructor takes several concrete (non-interface) dependencies - TimerService,
    /// TripPlannerService, MqttPublisher, HttpPublisher, SlackConnector, MqttConnector -
    /// none of which perform I/O purely by being constructed, so this is safe to build
    /// without any real network/broker calls as long as callers never invoke
    /// Conductor.StartAsync() (which calls IAwtrixApp.Init(), which always publishes via
    /// the real MQTT/HTTP publishers - a testability blocker documented in the final report).
    /// </summary>
    internal static class ConductorTestHelper
    {
        public static Conductor Create(AwtrixConfig? config = null, IHostEnvironment? hostEnvironment = null)
        {
            config ??= new AwtrixConfig { Devices = Array.Empty<DeviceConfig>() };

            var env = hostEnvironment;
            if (env == null)
            {
                var envMock = new Mock<IHostEnvironment>();
                envMock.Setup(e => e.EnvironmentName).Returns("Production");
                env = envMock.Object;
            }

            var timerService = new TimerService(NullLogger<TimerService>.Instance);
            var tripPlanner = new TripPlannerService(
                new StopfinderClient(new HttpClient()),
                new TripClient(new HttpClient()),
                NullLogger<TripPlannerService>.Instance);
            var mqttConnector = new MqttConnector(NullLogger<MqttConnector>.Instance, Options.Create(new MqttSettings()));
            var mqttPublisher = new MqttPublisher(mqttConnector, NullLogger<MqttPublisher>.Instance);
            var httpPublisher = new HttpPublisher(
                NullLogger<HttpPublisher>.Instance,
                new StubHttpClientFactory(StubHttpMessageHandler.Returning(HttpStatusCode.OK)));
            var slackConnector = new SlackConnector(NullLogger<SlackConnector>.Instance);

            return new Conductor(
                NullLogger<Conductor>.Instance,
                env,
                Options.Create(config),
                timerService,
                tripPlanner,
                mqttPublisher,
                httpPublisher,
                slackConnector,
                mqttConnector,
                NullLoggerFactory.Instance);
        }
    }
}
