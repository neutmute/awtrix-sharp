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
        private readonly TripPlannerService _tripPlanner;
        private readonly TimerService _timerService;
        AwtrixConfig _awtrixConfig;

        List<AwtrixApp> _apps;

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

            _apps = new List<AwtrixApp>();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var awtrixService = new AwtrixService(_httpPublisher, _mqttConnector);
            var clock = new Clock();
            foreach (var device in _awtrixConfig.Devices)
            {
                foreach(var appConfig in device.Apps)
                {
                    AwtrixApp app = null;
                    switch(appConfig.Name)
                    {
                        case "TripTimerApp":
                            var tripTimerConfig = appConfig.As<TripTimerAppConfig>();

                            tripTimerConfig.CronSchedule = "*/1 * * * *"; // Every minute
                            tripTimerConfig.ActiveTime = TimeSpan.FromSeconds(30);

                            app = new TripTimerApp(_logger, clock, device, awtrixService, _timerService, tripTimerConfig, _tripPlanner);
                            break;
                    }
                    if (app == null)
                    {
                        _logger.LogWarning($"App {appConfig.Name} is not implemented.");
                        continue;
                    }
                    app.Initialize();
                    _apps.Add(app);
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
