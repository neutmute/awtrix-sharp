using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Domain;
using Microsoft.Extensions.Logging;

namespace AwtrixSharpWeb
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Configure logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.AddDebug();

            // Configure MQTT settings
            builder.Services.Configure<MqttSettings>(
                builder.Configuration.GetSection("Mqtt"));

            builder.Services.AddControllers();
            builder.Services.AddOpenApi();
            builder.Services.AddHostedService<MqttService>();

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            app.UseHttpsRedirection();

            app.MapControllers();

            app.Run();
        }
    }
}
