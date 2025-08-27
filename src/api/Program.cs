using AwtrixSharpWeb.Apps;
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;
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

            // Configure JSON serialization options for ValueMap support
            builder.Services.Configure<JsonSerializerOptions>(options =>
            {
                options.Converters.Add(new ValueMapJsonConverter());
                options.PropertyNameCaseInsensitive = true;
            });

            // Configure environment variables with AwtrixSharp prefix
            builder
                .Configuration
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables("AWTRIXSHARP_");

            // Configure logging
            var logging = builder.Logging;
            logging.ClearProviders();
            logging.AddSimpleConsole(options =>
            {
                options.TimestampFormat = "HH:mm:ss ";
                options.SingleLine = true;
            });
            logging.AddDebug();

            var services = builder.Services;

            // Configure MQTT settings
            services.Configure<MqttSettings>(
                builder.Configuration.GetSection("Mqtt"));

            services.Configure<AwtrixConfig>(
                builder.Configuration.GetSection("Awtrix"));
                
            // Configure Trip Planner settings
            services.Configure<TransportOpenDataConfig>(config => {
                config.ApiKey = Environment.GetEnvironmentVariable("TRANSPORTOPENDATA__APIKEY") ?? "";
                config.BaseUrl = builder.Configuration.GetSection("TransportOpenData:BaseUrl").Value ?? "https://api.transport.nsw.gov.au/v1/tp";
            });

            services
                .AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.Converters.Add(new ValueMapJsonConverter());
                    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                });

            services.AddTransient<AwtrixService>();
            services.AddTransient<TripPlannerService>();

            services.AddSingleton<MqttConnector>();
            services.AddSingleton<SlackConnector>();
            services.AddSingleton<HttpPublisher>();
            services.AddSingleton<MqttPublisher>();
            services.AddSingleton<Conductor>();
            services.AddSingleton<TimerService>();
            

            // Register the Trip Planner clients with HTTP client factory
            services.AddHttpClient<StopfinderClient>((serviceProvider, client) => {
                var config = serviceProvider.GetRequiredService<IOptions<TransportOpenDataConfig>>();
                
                // Set the authorization header
                client.DefaultRequestHeaders.Add("Authorization", $"apikey {config.Value.ApiKey}");
            });
            
            services.AddHttpClient<TripClient>((serviceProvider, client) => {
                var config = serviceProvider.GetRequiredService<IOptions<TransportOpenDataConfig>>();
                
                // Set the authorization header
                client.DefaultRequestHeaders.Add("Authorization", $"apikey {config.Value.ApiKey}");
            });

            services.AddHostedService(sp => sp.GetService<MqttConnector>());
            services.AddHostedService(sp => sp.GetService<SlackConnector>());
            services.AddHostedService(sp => sp.GetService<Conductor>());
            services.AddHostedService(sp => sp.GetService<TimerService>());

            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen();

            var app = builder.Build();


            // Get logger for startup information
            var logger = app.Services.GetRequiredService<ILogger<Program>>();

            // Log startup information
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            logger.LogInformation("Starting AwtrixSharpWeb v{Version}, {Commit}", version, GetGitCommitShort());
            logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);

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

        public static string? GetGitCommitShort() =>
            Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "GitCommitShort")?.Value;
    }

}
