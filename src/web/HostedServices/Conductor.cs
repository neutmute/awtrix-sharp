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
        AwtrixConfig _awtrixConfig;

        List<AwtrixApp> _apps;

        public Conductor(ILogger<Conductor> logger, IOptions<AwtrixConfig> awtrixConfig, MqttPublisher mqttConnector, HttpPublisher httpPublisher, SlackConnector slackConnector)
        {
            _logger = logger;
            _awtrixConfig = awtrixConfig.Value;
            _slackConnector = slackConnector;
            _httpPublisher = httpPublisher;
            _mqttConnector = mqttConnector;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var awtrixService = new AwtrixService(_httpPublisher, _mqttConnector);

            foreach(var device in _awtrixConfig.Devices)
            {
                foreach(var appConfig in device.Apps)
                {
                    AwtrixApp app = null;
                    switch(appConfig.Name)
                    {
                        case "TripTimerApp":
                         //   var app = 
                            break;
                    }
                    app.Initialize();
                }
            }

            //var app = new SlackStatusApp(_awtrixConfig.Value.Devices[0], _slackConnector, awtrixService);
            //_apps = new List<SlackStatusApp> { app };

            //app.Initialize();

            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Conductor stopped.");
            return Task.CompletedTask;
        }
    }
}
