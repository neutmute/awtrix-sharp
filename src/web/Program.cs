using AwtrixSharpWeb.Apps;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace AwtrixSharpWeb
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);


            // Configure environment variables with AwtrixSharp prefix
            builder
                .Configuration
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables("AWTRIXSHARP_");

            // Configure logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.AddDebug();

            var services = builder.Services;

            // Configure MQTT settings
            services.Configure<MqttSettings>(
                builder.Configuration.GetSection("Mqtt"));

            services.Configure<AwtrixConfig>(
                builder.Configuration.GetSection("Awtrix"));

            services.AddControllers();

            services.AddTransient<AwtrixService>();
            services.AddSingleton<MqttConnector>();
            services.AddSingleton<SlackConnector>();
            services.AddSingleton<HttpPublisher>();
            services.AddSingleton<MqttPublisher>();
            services.AddSingleton<Conductor>();

            services.AddHostedService(sp => sp.GetService<MqttConnector>());
            services.AddHostedService(sp => sp.GetService<SlackConnector>());
            services.AddHostedService(sp => sp.GetService<Conductor>());

            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen();

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();

                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.MapControllers();

            app.Run();
        }
    }
}
