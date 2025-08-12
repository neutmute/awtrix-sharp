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

            services.AddControllers(); 
          //  services.AddOpenApi();
            services.AddHostedService<MqttService>();

            //services.AddSwaggerGen(c => {
            //    c.SwaggerDoc("swagger.json", new OpenApiInfo
            //    {
            //        Title = "AwtrixSharp",
            //        Version = "v1"
            //    });
            //});

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
