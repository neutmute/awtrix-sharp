using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Domain;
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
            services.AddSingleton<MqttService>();
            services.AddSingleton<SlackSocketService>();
            services.AddHostedService(sp => sp.GetService<MqttService>());
            services.AddHostedService(sp => sp.GetService<SlackSocketService>());

            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen();

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();

                app.UseSwagger();
                app.UseSwaggerUI();

                //app.MapOpenApi();
            }

            app.UseHttpsRedirection();

            app.MapControllers();

            app.Run();
        }
    }
}
