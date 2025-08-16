using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AwtrixSharpWeb.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DiagnosticsController : ControllerBase
    {
        private readonly ILogger<DiagnosticsController> _logger;
        private readonly AwtrixConfig _awtrixConfig;
        private readonly MqttConnector _mqttService;
        private readonly AwtrixService _awtrixService;

        public DiagnosticsController(
            ILogger<DiagnosticsController> logger
            , IOptions<AwtrixConfig> devices
            , MqttConnector mqttService
            , AwtrixService awtrixService
            )
        {
            _awtrixConfig = devices.Value;
            _logger = logger;
            _mqttService = mqttService;
            _awtrixService = awtrixService;
        }

        [HttpGet("")]
        public IActionResult Get()
        {
            var diagnosticInfo = new
            {
                Timestamp = DateTime.UtcNow,
                Message = "Hello, world"
            };

            var payload = System.Text.Json.JsonSerializer.Serialize(diagnosticInfo);

            return Ok(diagnosticInfo);
        }

        [HttpPost("mqtt")]
        public async Task<IActionResult> Mqtt()
        {
            var diagnosticInfo = new
            {
                Timestamp = DateTime.UtcNow,
                Service = "AwtrixSharp",
                Status = "Running",
                Message = "Diagnostic test"
            };
            
            var payload = System.Text.Json.JsonSerializer.Serialize(diagnosticInfo);
            
            await _mqttService.PublishAsync("awtrixsharp/diagnostic", payload);
            
            return Ok(diagnosticInfo);
        }

        [HttpPost("awtrix")]
        public async Task<IActionResult> Awtrix()
        {
            foreach(var device in _awtrixConfig.Devices)
            {
                if (string.IsNullOrEmpty(device.BaseTopic))
                {
                    _logger.LogWarning("Device {Device} has an empty BaseTopic", device);
                    continue;
                }

                var payload = new AwtrixAppMessage()
                                    .SetText("AwtrixSharp")
                                    .SetRainbow(true)
                                    .SetDuration(TimeSpan.FromSeconds(5));

                await _awtrixService.Notify(device, payload);
            }

            return Ok();
        }
    }
}
