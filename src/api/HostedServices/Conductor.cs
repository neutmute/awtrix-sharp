using AwtrixSharpWeb.Apps;
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AwtrixSharpWeb.HostedServices
{
    internal class AppNames
    {
        public const string DiurnalApp = "DiurnalApp";
        public const string TripTimerApp = "TripTimerApp";
        public const string SlackStatusApp = "SlackStatusApp";
        public const string MqttRenderApp = "MqttRenderApp";
    }

    public class Conductor : IHostedService
    {
        private readonly ILogger<Conductor> _logger;
        private readonly SlackConnector _slackConnector;
        private readonly MqttConnector _mqttConnector;
        private readonly HttpPublisher _httpPublisher;
        private readonly MqttPublisher _mqttPublisher;
        private readonly TripPlannerService _tripPlanner;
        private readonly TimerService _timerService;
        private readonly IHostEnvironment _hostEnvironment;
        AwtrixConfig _awtrixConfig;

        List<IAwtrixApp> _apps;


        public Conductor(
            ILogger<Conductor> logger
            , IHostEnvironment env
            , IOptions<AwtrixConfig> awtrixConfig
            , TimerService timerService
            , TripPlannerService tripPlanner
            , MqttPublisher mqttPublisher
            , HttpPublisher httpPublisher
            , SlackConnector slackConnector
            , MqttConnector mqttConnector)
        {
            _logger = logger;
            _awtrixConfig = awtrixConfig.Value;
            _slackConnector = slackConnector;
            _httpPublisher = httpPublisher;
            _mqttPublisher = mqttPublisher;
            _mqttConnector = mqttConnector;
            _tripPlanner = tripPlanner;
            _timerService = timerService;
            _hostEnvironment = env;

            _apps = new List<IAwtrixApp>();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var device in _awtrixConfig.Devices)
            {
                var diurnalConfig = AppConfig.Empty(_hostEnvironment.EnvironmentName).SetName(AppNames.DiurnalApp);
                _apps.Add(AppFactory(device, diurnalConfig));

                foreach (var appConfig in device.Apps)
                {
                    var app = AppFactory(device, appConfig);            
                    _apps.Add(app);
                }

                foreach(var app in _apps)
                {
                    app.Init();
                }
            }

            if (_hostEnvironment.IsDevelopment())
            {
                // Execute the TripTimerApp immediately in development mode
                //((TripTimerApp)_apps.First(a => a is TripTimerApp)).ExecuteNow();
            }

            return Task.CompletedTask;
        }

        private IAwtrixApp AppFactory(DeviceConfig device, AppConfig appConfig)
        {
            IAwtrixApp app;

            var awtrixService = new AwtrixService(_httpPublisher, _mqttPublisher);
            var clock = new Clock();

            var isDev = _hostEnvironment.IsDevelopment();
            _logger.LogInformation("Environment: {EnvName}, isDev={isDev}", _hostEnvironment.EnvironmentName, isDev);

            switch (appConfig.Name)
            {
                case AppNames.DiurnalApp:
                    app = new DiurnalApp(_logger, _timerService, appConfig, device, awtrixService);
                    break;

                case AppNames.TripTimerApp:
                    var tripTimerConfig = appConfig.As<TripTimerAppConfig>();
                    app = new TripTimerApp(_logger, clock, device, awtrixService, _timerService, tripTimerConfig, _tripPlanner);
                    break;

                case AppNames.MqttRenderApp:
                    var mqttConfig = appConfig.As<MqttAppConfig>();
                    app = new MqttRenderApp(_logger, clock, mqttConfig, device, awtrixService, _mqttConnector);
                    break;

                case AppNames.SlackStatusApp:
                    var slackStatusConfig = appConfig.As<SlackStatusAppConfig>();
                    app = new SlackStatusApp(_logger, slackStatusConfig, device, awtrixService, _slackConnector);
                    break;

                default:
                   throw new NotImplementedException(appConfig.Name);
            }

            return app;
        }

        public void ExecuteNow(string baseTopic, string appName)
        {
            var device = _awtrixConfig.Devices.First(d => d.BaseTopic == baseTopic);
            var config = device.Apps.First(a => a.Name == appName);

            var app = AppFactory(device, config);
            app.Init();
            app.ExecuteNow();
        }

        public List<IAwtrixApp> FindApps(string appName)
        {
            var app = _apps.FindAll(a => a.GetConfig().Name == appName);
            return app;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Conductor stopped.");
            return Task.CompletedTask;
        }
    }
}
