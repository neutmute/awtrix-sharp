using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.Diurnal;
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Apps.SlackStatus;
using AwtrixSharpWeb.Apps.TripTimer;
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
        public const string ButtonApp = "ButtonApp";
        public const string TripTimerApp = "TripTimerApp";
        public const string SlackStatusApp = "SlackStatusApp";
        public const string MqttRenderApp = "MqttRenderApp";
        public const string MqttClockRenderApp = "MqttClockRenderApp";
    }

    /// <summary>
    /// Orchestrates the various Awtrix apps based on configuration
    /// </summary>
    public class Conductor : IHostedService
    {
        private readonly ILogger<Conductor> _logger;
        private readonly ISlackConnector _slackConnector;
        private readonly IMqttConnector _mqttConnector;
        private readonly IAwtrixService _awtrixService;
        private readonly ITripPlannerService _tripPlanner;
        private readonly ITimerService _timerService;
        private readonly IClock _clock;
        private readonly IHostEnvironment _hostEnvironment;
        private readonly ILoggerFactory _loggerFactory;
        AwtrixConfig _awtrixConfig;

        List<IAwtrixApp> _apps;

        public Conductor(
            ILogger<Conductor> logger
            , IHostEnvironment env
            , IOptions<AwtrixConfig> awtrixConfig
            , ITimerService timerService
            , ITripPlannerService tripPlanner
            , IAwtrixService awtrixService
            , ISlackConnector slackConnector
            , IMqttConnector mqttConnector
            , IClock clock
            , ILoggerFactory loggerFactory)
        {
            _logger = logger;
            _awtrixConfig = awtrixConfig.Value;
            _slackConnector = slackConnector;
            _awtrixService = awtrixService;
            _mqttConnector = mqttConnector;
            _tripPlanner = tripPlanner;
            _timerService = timerService;
            _clock = clock;
            _hostEnvironment = env;
            _loggerFactory = loggerFactory;

            _apps = new List<IAwtrixApp>();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var device in _awtrixConfig.Devices)
            {
                ButtonApp? buttonApp = null;

                if (device.IsHttp)
                {
                    _logger.LogInformation(
                        "Device {Device} uses the HTTP transport; hardware buttons are not supported, ButtonApp not created",
                        device.BaseTopic);
                }
                else
                {
                    buttonApp = (ButtonApp)AppFactory(device, AppConfig.Empty().WithName(AppNames.ButtonApp));
                    _apps.Add(buttonApp);

                    buttonApp.Click += (s, e) =>
                    {
                        _logger.LogInformation("{Button} button clicked on {Device}", e.Button, device.BaseTopic);
                    };

                    buttonApp.DoubleClick += (s, e) =>
                    {
                        _logger.LogInformation("{Button} button double-clicked on {Device}", e.Button, device.BaseTopic);
                    };
                }

                foreach (var appConfig in device.Apps)
                {
                    // Log the app configuration to debug configuration binding issues
                    LogAppConfigDetails(appConfig);

                    var app = AppFactory(device, appConfig);

                    // Hacky binding for now
                    if (app is TripTimerApp tripTimerApp && buttonApp != null)
                    {
                        buttonApp.DoubleClick += (s, e) =>
                        {
                            if (e.Button == Button.Right)
                            {
                                tripTimerApp.ExecuteNow();
                            }
                        };
                    }

                    _apps.Add(app);
                }

                foreach (var app in _apps)
                {
                    await app.InitAsync();
                }
            }
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

            _logger.LogInformation(
                "Creating {AppName} for {device}"
                , appConfig.Name
                , device.BaseTopic);

            switch (appConfig.Type)
            {
                case AppNames.DiurnalApp:
                    {
                        var appLogger = _loggerFactory.CreateLogger<DiurnalApp>();
                        app = new DiurnalApp(appLogger, _clock, _timerService, appConfig, device, _awtrixService);
                    }
                    break;

                case AppNames.TripTimerApp:
                    {
                        var appLogger = _loggerFactory.CreateLogger<TripTimerApp>();
                        var tripTimerConfig = appConfig.As<TripTimerAppConfig>();
                        app = new TripTimerApp(appLogger, _clock, device, _awtrixService, _timerService, tripTimerConfig, _tripPlanner);
                    }
                    break;

                case AppNames.ButtonApp:
                    {
                        var appLogger = _loggerFactory.CreateLogger<MqttRenderApp>();
                        var mqttConfig = appConfig.As<MqttAppConfig>();
                        app = new ButtonApp(appLogger, mqttConfig, device, _awtrixService, _mqttConnector);
                    }
                    break;

                case AppNames.MqttRenderApp:
                    {
                        var appLogger = _loggerFactory.CreateLogger<MqttRenderApp>();
                        var mqttConfig = appConfig.As<MqttAppConfig>();
                        app = new MqttRenderApp(appLogger, _clock, mqttConfig, device, _awtrixService, _mqttConnector);
                    }
                    break;

                case AppNames.MqttClockRenderApp:
                    {
                        var appLogger = _loggerFactory.CreateLogger<MqttClockRenderApp>();
                        var mqttConfig = appConfig.As<MqttAppConfig>();
                        app = new MqttClockRenderApp(appLogger, _clock, mqttConfig, device, _awtrixService, _mqttConnector, _timerService);
                    }
                    break;

                case AppNames.SlackStatusApp:
                    {
                        var appLogger = _loggerFactory.CreateLogger<SlackStatusApp>();
                        var slackStatusConfig = appConfig.As<SlackStatusAppConfig>();
                        app = new SlackStatusApp(appLogger, slackStatusConfig, device, _awtrixService, _slackConnector);
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
                app.InitAsync().GetAwaiter().GetResult();
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
            _logger.LogInformation("Conductor stopping");

            foreach (var app in _apps)
            {
                try
                {
                    app.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing {AppType} on {Device}", app.GetConfig()?.Type, app.AwtrixAddress);
                }
            }

            return Task.CompletedTask;
        }
    }
}
