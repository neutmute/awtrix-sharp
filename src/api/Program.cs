using AwtrixSharpWeb.Apps;
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
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


            // Configure Trip Planner settings
            services.Configure<TransportOpenDataConfig>(config =>
            {
                config.ApiKey = Environment.GetEnvironmentVariable("TRANSPORTOPENDATA__APIKEY") ?? "";
                config.BaseUrl = builder.Configuration.GetSection("TransportOpenData:BaseUrl").Value ?? "https://api.transport.nsw.gov.au/v1/tp";
            });

            services.AddControllers();

            services.AddTransient<AwtrixService>();
            services.AddTransient<TripPlannerService>();

            services.AddSingleton<MqttConnector>();
            services.AddSingleton<SlackConnector>();
            services.AddSingleton<HttpPublisher>();
            services.AddSingleton<MqttPublisher>();
            services.AddSingleton<Conductor>();
            services.AddSingleton<TimerService>();

            // Register the Trip Planner clients with HTTP client factory
            services.AddHttpClient<StopfinderClient>((serviceProvider, client) =>
            {
                var config = serviceProvider.GetRequiredService<IOptions<TransportOpenDataConfig>>();

                // Set the authorization header
                client.DefaultRequestHeaders.Add("Authorization", $"apikey {config.Value.ApiKey}");
            });

            services.AddHttpClient<TripClient>((serviceProvider, client) =>
            {
                var config = serviceProvider.GetRequiredService<IOptions<TransportOpenDataConfig>>();

                // Set the authorization header
                client.DefaultRequestHeaders.Add("Authorization", $"apikey {config.Value.ApiKey}");
            });

            services.AddHostedService(sp => sp.GetService<MqttConnector>());
            services.AddHostedService(sp => sp.GetService<SlackConnector>());
            services.AddHostedService(sp => sp.GetService<Conductor>());
            services.AddHostedService(sp => sp.GetService<TimerService>());

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

        private static void SetupConfiguration(ConfigurationManager configuration, IServiceCollection services)
        {
            configuration
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables("AWTRIXSHARP_");

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
