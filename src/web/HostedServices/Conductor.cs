using AwtrixSharpWeb.Apps;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Options;

namespace AwtrixSharpWeb.HostedServices
{
    public class Conductor : IHostedService
    {
        private readonly ILogger<Conductor> _logger;
        private readonly SlackConnector _slackConnector;
        private readonly HttpPublisher _httpPublisher;
        private readonly MqttPublisher _mqttConnector;
        IOptions<AwtrixConfig> _awtrixConfig;

        List<SlackStatusApp> _apps;
        public Conductor(ILogger<Conductor> logger, IOptions<AwtrixConfig> awtrixConfig, MqttPublisher mqttConnector, HttpPublisher httpPublisher, SlackConnector slackConnector)
        {
            _logger = logger;
            _awtrixConfig = awtrixConfig;
            _slackConnector = slackConnector;
            _httpPublisher = httpPublisher;
            _mqttConnector = mqttConnector;
        }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            var awtrixService = new AwtrixService(_httpPublisher, _mqttConnector);
            var app = new SlackStatusApp(_awtrixConfig.Value.Devices[0], _slackConnector, awtrixService);
            _apps = new List<SlackStatusApp> { app };

            app.Initialize();

            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Conductor stopped.");
            return Task.CompletedTask;
        }
    }
}
