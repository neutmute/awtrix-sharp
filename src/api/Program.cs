using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Middleware;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using System.Reflection;
using TransportOpenData;
using TransportOpenData.TripPlanner;

namespace AwtrixSharpWeb
{
    public class Program
    {
        /// <summary>
        /// The local-only environment name that targets the AWTRIX NG simulator. Running under this
        /// environment must never be able to reach real hardware or brokers.
        /// </summary>
        public const string SimulatorEnvironmentName = "Simulator";

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var configuration = builder.Configuration;

            // .NET 10's WebApplication.CreateBuilder only enables static web assets (which is how
            // Microsoft.AspNetCore.App.Internal.Assets serves _framework/blazor.web.js) in the
            // Development environment. Any other environment run from source (Simulator, or an ad-hoc
            // local environment) also hosts the Blazor test UI, so it needs the same static web assets
            // wired up or the page prerenders but never becomes interactive (blazor.web.js 404s).
            // Production runs from a publish output, where the assets are physically in wwwroot.
            if (!builder.Environment.IsProduction())
            {
                builder.WebHost.UseStaticWebAssets();
            }

            var services = builder.Services;

            SetupConfiguration(configuration, services, builder.Environment.EnvironmentName);

            ConfigureLogging(builder);

            AddHttpSurface(services, configuration);

            AddAwtrixServices(services, configuration, builder.Environment.EnvironmentName);

            var app = builder.Build();

            LogStartup(app);

            ConfigureHttpPipeline(app);

            app.Run();
        }

        /// <summary>
        /// MVC, ProblemDetails, API-key options and Swagger generation. Public so tests can host the pipeline.
        /// </summary>
        public static void AddHttpSurface(IServiceCollection services, IConfiguration configuration)
        {
            services.AddControllers();
            services.AddRazorComponents().AddInteractiveServerComponents();
            services.AddProblemDetails();
            services.Configure<ApiSettings>(configuration.GetSection(ApiSettings.SectionName));
            RegisterSwagger(services, configuration);
        }

        /// <summary>
        /// CR-15: stack traces only in Development; ProblemDetails otherwise. Swagger is optional (default on),
        /// and sits before the optional API-key check so its UI stays reachable.
        /// </summary>
        public static void ConfigureHttpPipeline(WebApplication app)
        {
            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler();
            }

            if (IsSwaggerEnabled(app.Configuration, app.Logger))
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            // The test UI is open like Swagger; ApiKeyMiddleware also skips /ui, /_blazor and /_framework by path.
            app.UseStaticFiles();
            app.UseAntiforgery();

            app.UseMiddleware<ApiKeyMiddleware>();

            app.MapControllers();
            app.MapRazorComponents<Ui.App>().AddInteractiveServerRenderMode();
        }

        /// <summary>
        /// Swagger:Enabled (AWTRIXSHARP_SWAGGER__ENABLED). Blank or unparseable means enabled, as it always was.
        /// </summary>
        internal static bool IsSwaggerEnabled(IConfiguration configuration, ILogger logger)
        {
            var raw = configuration["Swagger:Enabled"];
            if (string.IsNullOrWhiteSpace(raw))
            {
                return true;
            }

            if (bool.TryParse(raw, out var enabled))
            {
                return enabled;
            }

            logger.LogWarning("Swagger:Enabled value '{Value}' is not true or false; Swagger stays enabled", raw);
            return true;
        }

        /// <summary>
        /// Application services (everything except MVC and Swagger). Public so the DI graph can be validated in tests.
        /// <paramref name="environmentName"/> is optional so existing callers (including
        /// <c>CompositionRootTests</c> and <c>SlackWiringTests</c>) keep compiling unchanged; leaving it null behaves
        /// like every non-Simulator environment (literal environment-variable fallbacks stay active).
        /// </summary>
        public static void AddAwtrixServices(IServiceCollection services, IConfiguration configuration, string? environmentName = null)
        {
            // Settings and secrets: IConfiguration first, literal environment variable as fallback (CR-14) —
            // except in the Simulator environment, which must never read real credentials out of the process
            // environment (see AddSettings).
            AddSettings(services, configuration, environmentName);

            // Time
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IClock, Clock>();

            // Trip planner: a singleton that builds NSwag clients per call over this named client (CR-29)
            services.AddHttpClient(TripPlannerService.HttpClientName, (serviceProvider, client) =>
            {
                var config = serviceProvider.GetRequiredService<IOptions<TransportOpenDataConfig>>();

                client.Timeout = TripPlannerService.HttpTimeout;
                client.DefaultRequestHeaders.Add("Authorization", $"apikey {config.Value.ApiKey}");
            });
            services.AddSingleton<TripPlannerService>();
            services.AddSingleton<ITripPlannerService>(sp => sp.GetRequiredService<TripPlannerService>());

            // Connectors
            services.AddSingleton<MqttConnector>();
            services.AddSingleton<IMqttConnector>(sp => sp.GetRequiredService<MqttConnector>());
            services.AddSingleton<DeviceStateMonitor>();
            services.AddSingleton<SlackConnector>();
            services.AddSingleton<ISlackConnector>(sp => sp.GetRequiredService<SlackConnector>());

            // Publishing
            services.AddHttpClient(HttpPublisher.HttpClientName, client => client.Timeout = HttpPublisher.DefaultTimeout);
            services.AddSingleton<HttpPublisher>();
            services.AddSingleton<MqttPublisher>();
            services.AddSingleton<PublishTrace>();
            services.AddSingleton<IAwtrixService>(sp => new AwtrixService(
                sp.GetRequiredService<HttpPublisher>(),
                sp.GetRequiredService<MqttPublisher>(),
                sp.GetRequiredService<ILogger<AwtrixService>>(),
                sp.GetRequiredService<PublishTrace>(),
                sp.GetRequiredService<TimeProvider>()));

            // Orchestration
            services.AddSingleton<TimerService>();
            services.AddSingleton<ITimerService>(sp => sp.GetRequiredService<TimerService>());
            services.AddSingleton<Conductor>();

            // Hosted services start in this order and stop in reverse
            services.AddHostedService(sp => sp.GetRequiredService<MqttConnector>());
            services.AddHostedService(sp => sp.GetRequiredService<DeviceStateMonitor>());
            services.AddHostedService(sp => sp.GetRequiredService<SlackConnector>());
            services.AddHostedService(sp => sp.GetRequiredService<Conductor>());
            services.AddHostedService(sp => sp.GetRequiredService<TimerService>());
        }

        internal const string DefaultTransportOpenDataBaseUrl = "https://api.transport.nsw.gov.au/v1/tp";
        private const string LegacyTransportOpenDataApiRoot = "https://api.transport.nsw.gov.au/v1";
        internal const string TransportOpenDataApiKeyEnvironmentVariable = "TRANSPORTOPENDATA__APIKEY";

        /// <summary>
        /// <paramref name="environmentName"/> optional for existing callers (default: apply the literal
        /// environment-variable fallbacks, as every non-Simulator environment does). In the Simulator environment
        /// none of the three fallbacks below run: Simulator settings come only from appsettings.Simulator.json,
        /// never from real broker/Slack/TransportOpenData credentials sitting in the developer's process
        /// environment.
        /// </summary>
        private static void AddSettings(IServiceCollection services, IConfiguration configuration, string? environmentName = null)
        {
            var isSimulator = IsSimulatorEnvironment(environmentName);

            // Explicit reads rather than Bind: the API key needs the literal TRANSPORTOPENDATA__APIKEY fallback, and a
            // blank BaseUrl falls back to .../v1/tp. The named TransportOpenData HttpClient (WS6) reads ApiKey from these
            // options when each client is created; TripPlannerService reads BaseUrl per call. The literal fallback is
            // skipped entirely in the Simulator environment.
            services.AddOptions<TransportOpenDataConfig>().Configure(config =>
            {
                config.ApiKey = FirstNonBlank(
                    configuration["TransportOpenData:ApiKey"],
                    isSimulator ? null : Environment.GetEnvironmentVariable(TransportOpenDataApiKeyEnvironmentVariable)) ?? string.Empty;
                config.BaseUrl = NormaliseTransportOpenDataBaseUrl(FirstNonBlank(configuration["TransportOpenData:BaseUrl"]));
            });

            var slackOptions = services.AddOptions<SlackSettings>()
                .Bind(configuration.GetSection(SlackSettings.SectionName));
            if (!isSimulator)
            {
                slackOptions.PostConfigure(settings => settings.WithEnvironmentFallback());
            }

            var dataOptions = services.AddOptions<DataSettings>()
                .Configure(settings => settings.DataDirectory = configuration[DataSettings.DataDirectoryKey]);
            if (!isSimulator)
            {
                dataOptions.PostConfigure(settings => settings.WithEnvironmentFallback());
            }
        }

        /// <summary>
        /// Case-insensitive match against <see cref="SimulatorEnvironmentName"/>, shared by <see cref="SetupConfiguration(ConfigurationManager, IServiceCollection, string?)"/>
        /// and <see cref="AddSettings"/> so both configuration loading and the environment-variable fallbacks agree
        /// on what counts as the Simulator environment.
        /// </summary>
        internal static bool IsSimulatorEnvironment(string? environmentName) =>
            string.Equals(environmentName, SimulatorEnvironmentName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Blank falls back to the Trip Planner default. The legacy TfNSW API root (.../v1, the library's original default,
        /// ignored until CR-36) also means the Trip Planner: honouring it as-is requests /v1/trip, which TfNSW rejects with a
        /// 500 SOAP fault.
        /// </summary>
        internal static string NormaliseTransportOpenDataBaseUrl(string? baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)
                || string.Equals(baseUrl.Trim().TrimEnd('/'), LegacyTransportOpenDataApiRoot, StringComparison.OrdinalIgnoreCase))
            {
                return DefaultTransportOpenDataBaseUrl;
            }

            return baseUrl;
        }

        internal static string? FirstNonBlank(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        /// <summary>
        /// WebApplication.CreateBuilder already loads appsettings.json, appsettings.{Env}.json, user secrets,
        /// environment variables and the command line (in that order). Outside the Simulator environment the
        /// AWTRIXSHARP_ provider is added here, last, so it keeps overriding everything (CR-13: appsettings.json
        /// is not re-added). This overload has no environment name, so it always applies that non-Simulator path.
        /// </summary>
        internal static void SetupConfiguration(ConfigurationManager configuration, IServiceCollection services) =>
            SetupConfiguration(configuration, services, environmentName: null);

        /// <summary>
        /// As above, except in the local-only Simulator environment (<paramref name="environmentName"/> equal to
        /// <see cref="SimulatorEnvironmentName"/>), which must never reach real hardware or brokers:
        /// <list type="bullet">
        /// <item>The base appsettings.json source is removed. Configuration arrays merge by index, so otherwise the
        /// Simulator device would inherit the base file's extra apps (and their keys); appsettings.Simulator.json is
        /// self-contained.</item>
        /// <item>The AWTRIXSHARP_ environment-variable provider is not registered. Real broker/clock/Slack
        /// credentials are commonly exported as AWTRIXSHARP_ variables on developer machines, and since this
        /// provider is added last it would otherwise override appsettings.Simulator.json.</item>
        /// <item>WebApplication.CreateBuilder's own unprefixed environment-variable provider is removed too. Without
        /// this, a literally-named variable such as TRANSPORTOPENDATA__APIKEY still reaches IConfiguration as
        /// "TransportOpenData:ApiKey" (double underscore is that provider's own section separator), bypassing the
        /// AWTRIXSHARP_ scoping entirely. AddSettings' Slack/Data/TransportOpenData fallbacks (see
        /// <see cref="IsSimulatorEnvironment"/>) close the matching gap for their own literal
        /// Environment.GetEnvironmentVariable reads.</item>
        /// </list>
        /// Every other environment keeps the CR-13 behaviour.
        /// </summary>
        internal static void SetupConfiguration(ConfigurationManager configuration, IServiceCollection services, string? environmentName)
        {
            var isSimulator = IsSimulatorEnvironment(environmentName);

            if (isSimulator)
            {
                RemoveBaseAppSettings(configuration);
                RemoveEnvironmentVariableSources(configuration);
            }
            else
            {
                configuration.AddEnvironmentVariables("AWTRIXSHARP_");
            }

            services.Configure<MqttSettings>(configuration.GetSection("Mqtt"));
            services.Configure<AwtrixConfig>(configuration.GetSection("Awtrix"));
        }

        private static void RemoveBaseAppSettings(IConfigurationBuilder configuration)
        {
            var sources = configuration.Sources;
            for (var i = sources.Count - 1; i >= 0; i--)
            {
                if (sources[i] is JsonConfigurationSource json
                    && string.Equals(json.Path, "appsettings.json", StringComparison.OrdinalIgnoreCase))
                {
                    sources.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Removes every environment-variable configuration source WebApplication.CreateBuilder already added
        /// (its default unprefixed one, and the AWTRIXSHARP_ one if a caller added it before this runs). Real
        /// credentials can arrive unprefixed too (e.g. TRANSPORTOPENDATA__APIKEY), so the Simulator environment
        /// must not read the process environment through IConfiguration at all.
        /// </summary>
        private static void RemoveEnvironmentVariableSources(IConfigurationBuilder configuration)
        {
            var sources = configuration.Sources;
            for (var i = sources.Count - 1; i >= 0; i--)
            {
                if (sources[i] is EnvironmentVariablesConfigurationSource)
                {
                    sources.RemoveAt(i);
                }
            }
        }

        private static void ConfigureLogging(WebApplicationBuilder builder)
        {
            var logging = builder.Logging;
            logging.ClearProviders();
            logging.AddSimpleConsole(options =>
            {
                options.TimestampFormat = "HH:mm:ss ";
                options.SingleLine = true;
            });
            logging.AddDebug();
        }

        private static void RegisterSwagger(IServiceCollection services, IConfiguration configuration)
        {
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Awtrix API", Version = "v1" });

                // Enable annotations for Swagger
                c.EnableAnnotations();

                // Let "Try it out" send the optional API key
                if (!string.IsNullOrWhiteSpace(configuration[ApiSettings.KeyConfigurationKey]))
                {
                    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.ApiKey,
                        In = ParameterLocation.Header,
                        Name = ApiKeyMiddleware.HeaderName,
                        Description = "API key configured in Api:Key",
                    });
                    c.AddSecurityRequirement(new OpenApiSecurityRequirement
                    {
                        {
                            new OpenApiSecurityScheme
                            {
                                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" },
                            },
                            Array.Empty<string>()
                        },
                    });
                }
            });
        }

        private static void LogStartup(WebApplication app)
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            logger.LogInformation("Starting AwtrixSharp v{Version}, {Commit}", version, GetGitCommitShort());
            logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);

            var warnings = ConfigurationWarnings.Get(
                app.Services.GetRequiredService<IOptions<AwtrixConfig>>().Value,
                app.Services.GetRequiredService<IOptions<TransportOpenDataConfig>>().Value,
                app.Services.GetRequiredService<IOptions<SlackSettings>>().Value);

            foreach (var warning in warnings)
            {
                logger.LogWarning("Configuration: {Warning}", warning);
            }
        }

        public static string? GetGitCommitShort() =>
            Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "GitCommitShort")?.Value;
    }
}
