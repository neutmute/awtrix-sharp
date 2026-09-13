using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
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

            services.AddControllers();

            AddAwtrixServices(services, configuration);

            RegisterSwagger(services);

            var app = builder.Build();

            LogStartup(app);

            // if (app.Environment.IsDevelopment()) always show swagger
            {
                app.UseDeveloperExceptionPage();

                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.MapControllers();

            app.Run();
        }

        /// <summary>
        /// Application services (everything except MVC and Swagger). Public so the DI graph can be validated in tests.
        /// </summary>
        public static void AddAwtrixServices(IServiceCollection services, IConfiguration configuration)
        {
            // Configure Trip Planner settings
            services.Configure<TransportOpenDataConfig>(config =>
            {
                config.ApiKey = Environment.GetEnvironmentVariable("TRANSPORTOPENDATA__APIKEY") ?? "";
                config.BaseUrl = configuration.GetSection("TransportOpenData:BaseUrl").Value ?? "https://api.transport.nsw.gov.au/v1/tp";
            });

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

        private static void RegisterSwagger(IServiceCollection services)
        {
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Awtrix API", Version = "v1" });

                // Enable annotations for Swagger
                c.EnableAnnotations();
            });
        }

        private static void LogStartup(WebApplication app)
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            logger.LogInformation("Starting AwtrixSharp v{Version}, {Commit}", version, GetGitCommitShort());
            logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);
        }

        public static string? GetGitCommitShort() =>
            Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "GitCommitShort")?.Value;
    }
}
