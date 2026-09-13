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

        /// <summary>
        /// Every Type the factory can build, listed in "unknown app type" warnings.
        /// </summary>
        public static readonly string[] All = { DiurnalApp, ButtonApp, TripTimerApp, SlackStatusApp, MqttRenderApp, MqttClockRenderApp };
    }

    /// <summary>
    /// Orchestrates the various Awtrix apps based on configuration.
    /// Lifecycle: create every app, init each exactly once (isolated), bind buttons, register;
    /// on stop, dispose every registered app.
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
        private readonly AwtrixConfig _awtrixConfig;

        private readonly object _registryLock = new();
        private readonly List<RegisteredApp> _registry = new();
        private int _started;

        /// <summary>
        /// Per-app disposal budget: two sequential 5 s HTTP publishes (Dismiss + AppClear) to an offline device.
        /// </summary>
        internal static readonly TimeSpan AppDisposeTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// An app keyed by the device it drives and its configured Type.
        /// </summary>
        private sealed record RegisteredApp(AwtrixAddress? Device, string Type, IAwtrixApp App)
        {
            public string BaseTopic => Device?.BaseTopic ?? string.Empty;
        }

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
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _started, 1) == 1)
            {
                _logger.LogWarning("Conductor.StartAsync called more than once; ignoring");
                return;
            }

            // Phase 1: create every app for every device. Nothing is initialised yet.
            var created = CreateApps();

            // Phase 2: initialise each app exactly once; a failure is isolated to that app.
            var initialised = await Task.WhenAll(created.Select(InitOneAsync));
            var running = created.Where((_, index) => initialised[index]).ToList();

            // Phase 3: device-local bindings between running apps, then register.
            foreach (var deviceApps in running.GroupBy(r => r.Device))
            {
                BindButtons(deviceApps.ToList());
            }

            lock (_registryLock)
            {
                _registry.AddRange(running);
            }

            _logger.LogInformation("Conductor started {Running} of {Created} app(s)", running.Count, created.Count);
        }

        private List<RegisteredApp> CreateApps()
        {
            var created = new List<RegisteredApp>();

            foreach (var device in _awtrixConfig.Devices ?? Array.Empty<DeviceConfig>())
            {
                if (device == null || string.IsNullOrWhiteSpace(device.BaseTopic))
                {
                    _logger.LogWarning("Skipping a device with no BaseTopic");
                    continue;
                }

                if (device.IsHttp)
                {
                    _logger.LogInformation(
                        "Device {Device} uses the HTTP transport; hardware buttons are not supported, ButtonApp not created",
                        device.BaseTopic);
                }
                else
                {
                    AddIfCreated(created, device, AppConfig.Empty().WithName(AppNames.ButtonApp));
                }

                foreach (var appConfig in device.Apps ?? new List<AppConfig>())
                {
                    AddIfCreated(created, device, appConfig);
                }
            }

            return created;
        }

        private void AddIfCreated(List<RegisteredApp> created, DeviceConfig device, AppConfig? appConfig)
        {
            if (appConfig == null || string.IsNullOrWhiteSpace(appConfig.Type))
            {
                _logger.LogWarning("Skipping an app with no Type on device {Device}", device.BaseTopic);
                return;
            }

            LogAppConfigDetails(appConfig);

            try
            {
                var app = AppFactory(device, appConfig);
                if (app != null)
                {
                    created.Add(new RegisteredApp(device, appConfig.Type, app));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create app {AppType} on device {Device}: {Reason}; skipping", appConfig.Type, device.BaseTopic, ex.Message);
            }
        }

        private async Task<bool> InitOneAsync(RegisteredApp entry)
        {
            try
            {
                await entry.App.InitAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialise app {AppType} on device {Device}: {Reason}; the app will not run", entry.Type, entry.BaseTopic, ex.Message);
                await DisposeOneAsync(entry, CancellationToken.None);
                return false;
            }
        }

        private void BindButtons(IReadOnlyList<RegisteredApp> deviceApps)
        {
            var buttonApp = deviceApps.Select(r => r.App).OfType<ButtonApp>().FirstOrDefault();
            if (buttonApp == null)
            {
                return;
            }

            var baseTopic = deviceApps[0].BaseTopic;

            buttonApp.Click += (s, e) =>
            {
                _logger.LogInformation("{Button} button clicked on {Device}", e.Button, baseTopic);
            };

            buttonApp.DoubleClick += (s, e) =>
            {
                _logger.LogInformation("{Button} button double-clicked on {Device}", e.Button, baseTopic);
            };

            // Right double-click starts this device's trip timer now
            foreach (var tripTimerApp in deviceApps.Select(r => r.App).OfType<TripTimerApp>())
            {
                buttonApp.DoubleClick += (s, e) =>
                {
                    if (e.Button != Button.Right)
                    {
                        return;
                    }

                    try
                    {
                        tripTimerApp.ExecuteNow();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Starting TripTimerApp from a double-click failed on {Device}", baseTopic);
                    }
                };
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

        /// <summary>
        /// Builds an app for a known Type; returns null (logged) for an unknown Type.
        /// </summary>
        private IAwtrixApp? AppFactory(DeviceConfig device, AppConfig appConfig)
        {
            IAwtrixApp app;

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
                        var appLogger = _loggerFactory.CreateLogger<ButtonApp>();
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
                    _logger.LogWarning(
                        "Unknown app type '{AppType}' on device '{Device}'; skipping. Known types: {KnownTypes}",
                        appConfig.Type,
                        device.BaseTopic,
                        string.Join(", ", AppNames.All));
                    return null;
            }

            _logger.LogInformation("Created {AppType} for {Device}", appConfig.Type, device.BaseTopic);

            return app;
        }

        /// <summary>
        /// Runs the registered instance(s) of <paramref name="appType"/> on the device <paramref name="baseTopic"/> now.
        /// Never creates, initialises or disposes an app, and never throws.
        /// </summary>
        public AppExecutionResult ExecuteNow(string baseTopic, string appType)
        {
            if (string.IsNullOrWhiteSpace(baseTopic) || string.IsNullOrWhiteSpace(appType))
            {
                _logger.LogWarning("ExecuteNow requires a base topic and an app type (got '{BaseTopic}', '{AppType}')", baseTopic, appType);
                return AppExecutionResult.NotFound;
            }

            List<RegisteredApp> matches;
            lock (_registryLock)
            {
                matches = _registry.Where(r => Matches(r, appType, baseTopic)).ToList();
            }

            if (matches.Count == 0)
            {
                _logger.LogWarning("ExecuteNow: no running app '{AppType}' on device '{BaseTopic}'", appType, baseTopic);
                return AppExecutionResult.NotFound;
            }

            var result = AppExecutionResult.Started;
            foreach (var entry in matches)
            {
                try
                {
                    entry.App.ExecuteNow();
                    _logger.LogInformation("Executed app '{AppType}' on device '{BaseTopic}'", entry.Type, entry.BaseTopic);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing app '{AppType}' on device '{BaseTopic}'", entry.Type, entry.BaseTopic);
                    result = AppExecutionResult.Error;
                }
            }

            return result;
        }

        /// <summary>
        /// Running apps of the given Type, optionally limited to one device (ordinal comparison).
        /// </summary>
        public List<IAwtrixApp> FindApps(string appType, string? baseTopic = null)
        {
            lock (_registryLock)
            {
                return _registry
                    .Where(r => Matches(r, appType, baseTopic))
                    .Select(r => r.App)
                    .ToList();
            }
        }

        /// <summary>
        /// Test seam: registers an already-constructed app, keyed by its address and config Type.
        /// </summary>
        internal void RegisterApp(IAwtrixApp app)
        {
            lock (_registryLock)
            {
                _registry.Add(new RegisteredApp(app.AwtrixAddress, app.GetConfig()?.Type ?? string.Empty, app));
            }
        }

        private static bool Matches(RegisteredApp entry, string appType, string? baseTopic)
        {
            return string.Equals(entry.Type, appType, StringComparison.Ordinal)
                && (baseTopic == null || string.Equals(entry.BaseTopic, baseTopic, StringComparison.Ordinal));
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Conductor stopping");

            List<RegisteredApp> apps;
            lock (_registryLock)
            {
                apps = _registry.ToList();
                _registry.Clear();
            }

            await Task.WhenAll(apps.Select(entry => DisposeOneAsync(entry, cancellationToken)));

            _logger.LogInformation("Conductor stopped; disposed {Count} app(s)", apps.Count);
        }

        /// <summary>
        /// Awaits the app's DisposeAsync, bounded by <see cref="AppDisposeTimeout"/> and the shutdown token. Never throws.
        /// </summary>
        private async Task DisposeOneAsync(RegisteredApp entry, CancellationToken cancellationToken)
        {
            try
            {
                await entry.App.DisposeAsync().AsTask().WaitAsync(AppDisposeTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Disposing {AppType} on {Device} did not finish within {Timeout}; continuing shutdown", entry.Type, entry.BaseTopic, AppDisposeTimeout);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Shutdown timeout reached while disposing {AppType} on {Device}", entry.Type, entry.BaseTopic);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing {AppType} on {Device}", entry.Type, entry.BaseTopic);
            }
        }
    }
}
