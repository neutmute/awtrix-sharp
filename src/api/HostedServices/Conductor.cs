using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.Diurnal;
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Apps.SlackStatus;
using AwtrixSharpWeb.Apps.TripTimer;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Text.Json;

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
        private readonly ILoggerFactory _loggerFactory;
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
            , MqttConnector mqttConnector
            , ILoggerFactory loggerFactory)
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
            _loggerFactory = loggerFactory;

            _apps = new List<IAwtrixApp>();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var device in _awtrixConfig.Devices)
            {
                foreach (var appConfig in device.Apps)
                {
                    // Log the app configuration to debug configuration binding issues
                    LogAppConfigDetails(appConfig);

                    var app = AppFactory(device, appConfig);            
                    _apps.Add(app);
                }

                foreach(var app in _apps)
                {
                    app.Init();
                }
            }

            return Task.CompletedTask;
        }

        private void LogAppConfigDetails(AppConfig appConfig)
        {
            var keysCount = appConfig.Config?.Count ?? 0;
            var valueMapsCount = appConfig.ValueMaps?.Count ?? 0;

            _logger.LogDebug(
                "App configuration: Type={Type}, Name={Name}, Keys.Count={KeysCount}, ValueMaps.Count={ValueMapsCount}",
                appConfig.Type,
                appConfig.Name,
                keysCount,
                valueMapsCount
            );
        }

        private IAwtrixApp AppFactory(DeviceConfig device, AppConfig appConfig)
        {
            IAwtrixApp app;

            var awtrixService = new AwtrixService(_httpPublisher, _mqttPublisher);
            var clock = new Clock();

            var isDev = _hostEnvironment.IsDevelopment();

            _logger.LogInformation(
                "Creating {AppName} for {device}"
                ,appConfig.Name
                ,device.BaseTopic);

            switch (appConfig.Type)
            {
                case AppNames.DiurnalApp:
                    {
                        var appLogger = _loggerFactory.CreateLogger<DiurnalApp>();
                        app = new DiurnalApp(appLogger, _timerService, appConfig, device, awtrixService);
                    }
                    break;

                case AppNames.TripTimerApp:
                    {
                        var appLogger = _loggerFactory.CreateLogger<TripTimerApp>();
                        var tripTimerConfig = appConfig.As<TripTimerAppConfig>();
                        app = new TripTimerApp(appLogger, clock, device, awtrixService, _timerService, tripTimerConfig, _tripPlanner);
                    }
                    break;

                case AppNames.MqttRenderApp:
                    {
                        var appLogger = _loggerFactory.CreateLogger<MqttRenderApp>();
                        var mqttConfig = appConfig.As<MqttAppConfig>();
                        app = new MqttRenderApp(appLogger, clock, mqttConfig, device, awtrixService, _mqttConnector);
                    }
                    break;

                case AppNames.SlackStatusApp:
                    {
                        var appLogger = _loggerFactory.CreateLogger<SlackStatusApp>();
                        var slackStatusConfig = appConfig.As<SlackStatusAppConfig>();
                        app = new SlackStatusApp(appLogger, slackStatusConfig, device, awtrixService, _slackConnector);
                    }
                    break;

                default:
                   throw new NotImplementedException(appConfig.Type);
            }

            return app;
        }

        public void ExecuteNow(string baseTopic, string appName)
        {
            try
            {
                var device = _awtrixConfig.Devices.FirstOrDefault(d => d.BaseTopic == baseTopic);
                if (device == null)
                {
                    _logger.LogWarning("Device with base topic '{BaseTopic}' not found", baseTopic);
                    return;
                }

                var config = device.Apps.FirstOrDefault(a => a.Type == appName);
                if (config == null)
                {
                    _logger.LogWarning("App '{AppName}' not found for device '{BaseTopic}'", appName, baseTopic);
                    return;
                }

                var app = AppFactory(device, config);
                app.Init();
                app.ExecuteNow();
                _logger.LogInformation("Successfully executed app '{AppName}' on device '{BaseTopic}'", appName, baseTopic);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing app '{AppName}' on device '{BaseTopic}'", appName, baseTopic);
            }
        }

        public List<IAwtrixApp> FindApps(string appName)
        {
            var app = _apps.FindAll(a => a.GetConfig().Type == appName);
            return app;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Conductor stopped.");
            return Task.CompletedTask;
        }
    }
}
