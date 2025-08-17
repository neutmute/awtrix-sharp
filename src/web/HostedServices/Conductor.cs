using AwtrixSharpWeb.Apps;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
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
        private readonly TripPlannerService _tripPlanner;
        private readonly TimerService _timerService;
        AwtrixConfig _awtrixConfig;

        List<IAwtrixApp> _apps;

        public Conductor(
            ILogger<Conductor> logger
            , IOptions<AwtrixConfig> awtrixConfig
            , TimerService timerService
            , TripPlannerService tripPlanner
            , MqttPublisher mqttConnector
            , HttpPublisher httpPublisher
            , SlackConnector slackConnector)
        {
            _logger = logger;
            _awtrixConfig = awtrixConfig.Value;
            _slackConnector = slackConnector;
            _httpPublisher = httpPublisher;
            _mqttConnector = mqttConnector;
            _tripPlanner = tripPlanner;
            _timerService = timerService;

            _apps = new List<IAwtrixApp>();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var awtrixService = new AwtrixService(_httpPublisher, _mqttConnector);
            var clock = new Clock();
            foreach (var device in _awtrixConfig.Devices)
            {
                foreach(var appConfig in device.Apps)
                {
                    IAwtrixApp app;
                    switch(appConfig.Name)
                    {
                        case "TripTimerApp":
                            var tripTimerConfig = appConfig.As<TripTimerAppConfig>();

                            tripTimerConfig.CronSchedule = "*/1 * * * *"; // Every minute
                            tripTimerConfig.ActiveTime = TimeSpan.FromMinutes(30);

                            app = new TripTimerApp(_logger, clock, device, awtrixService, _timerService, tripTimerConfig, _tripPlanner);
                            break;

                        case "SlackStatusApp":
                            var slackStatusConfig = appConfig.As<AppConfig>();
                            app = new SlackStatusApp(_logger, slackStatusConfig, device, awtrixService, _slackConnector);
                            break;

                        default:
                            _logger.LogWarning($"App {appConfig.Name} is not implemented.");
                            continue;
                    }

                    app.Initialize();
                    _apps.Add(app);
                }
            }

            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Conductor stopped.");
            return Task.CompletedTask;
        }
    }
}
