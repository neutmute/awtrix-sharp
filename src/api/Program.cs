using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Middleware;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using System.Reflection;
using TransportOpenData;
using TransportOpenData.TripPlanner;

namespace AwtrixSharpWeb
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var configuration = builder.Configuration;

            var services = builder.Services;

            SetupConfiguration(configuration, services);

            ConfigureLogging(builder);

            AddHttpSurface(services, configuration);

            AddAwtrixServices(services, configuration);

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

            app.UseMiddleware<ApiKeyMiddleware>();

            app.MapControllers();
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
        /// </summary>
        public static void AddAwtrixServices(IServiceCollection services, IConfiguration configuration)
        {
            // Settings and secrets: IConfiguration first, literal environment variable as fallback (CR-14)
            AddSettings(services, configuration);

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
            services.AddSingleton<SlackConnector>();
            services.AddSingleton<ISlackConnector>(sp => sp.GetRequiredService<SlackConnector>());

            // Publishing
            services.AddHttpClient(HttpPublisher.HttpClientName, client => client.Timeout = HttpPublisher.DefaultTimeout);
            services.AddSingleton<HttpPublisher>();
            services.AddSingleton<MqttPublisher>();
            services.AddSingleton<IAwtrixService, AwtrixService>();

            // Orchestration
            services.AddSingleton<TimerService>();
            services.AddSingleton<ITimerService>(sp => sp.GetRequiredService<TimerService>());
            services.AddSingleton<Conductor>();

            // Hosted services start in this order and stop in reverse
            services.AddHostedService(sp => sp.GetRequiredService<MqttConnector>());
            services.AddHostedService(sp => sp.GetRequiredService<SlackConnector>());
            services.AddHostedService(sp => sp.GetRequiredService<Conductor>());
            services.AddHostedService(sp => sp.GetRequiredService<TimerService>());
        }

        internal const string DefaultTransportOpenDataBaseUrl = "https://api.transport.nsw.gov.au/v1/tp";
        internal const string TransportOpenDataApiKeyEnvironmentVariable = "TRANSPORTOPENDATA__APIKEY";

        private static void AddSettings(IServiceCollection services, IConfiguration configuration)
        {
            // Explicit reads rather than Bind: the API key needs the literal TRANSPORTOPENDATA__APIKEY fallback, and a
            // blank BaseUrl falls back to .../v1/tp. The named TransportOpenData HttpClient (WS6) reads ApiKey from these
            // options when each client is created; TripPlannerService reads BaseUrl per call.
            services.AddOptions<TransportOpenDataConfig>().Configure(config =>
            {
                config.ApiKey = FirstNonBlank(
                    configuration["TransportOpenData:ApiKey"],
                    Environment.GetEnvironmentVariable(TransportOpenDataApiKeyEnvironmentVariable)) ?? string.Empty;
                config.BaseUrl = FirstNonBlank(configuration["TransportOpenData:BaseUrl"]) ?? DefaultTransportOpenDataBaseUrl;
            });

            services.AddOptions<SlackSettings>()
                .Bind(configuration.GetSection(SlackSettings.SectionName))
                .PostConfigure(settings => settings.WithEnvironmentFallback());

            services.AddOptions<DataSettings>()
                .Configure(settings => settings.DataDirectory = configuration[DataSettings.DataDirectoryKey])
                .PostConfigure(settings => settings.WithEnvironmentFallback());
        }

        internal static string? FirstNonBlank(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        /// <summary>
        /// WebApplication.CreateBuilder already loads appsettings.json, appsettings.{Env}.json, user secrets,
        /// environment variables and the command line (in that order). Only the AWTRIXSHARP_ provider is added
        /// here, last, so it keeps overriding everything (CR-13: appsettings.json is not re-added).
        /// </summary>
        internal static void SetupConfiguration(ConfigurationManager configuration, IServiceCollection services)
        {
            configuration.AddEnvironmentVariables("AWTRIXSHARP_");

            services.Configure<MqttSettings>(configuration.GetSection("Mqtt"));
            services.Configure<AwtrixConfig>(configuration.GetSection("Awtrix"));
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
