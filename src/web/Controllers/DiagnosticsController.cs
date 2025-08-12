using AwtrixSharpWeb.Services;
using Microsoft.AspNetCore.Mvc;

namespace AwtrixSharpWeb.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DiagnosticsController : ControllerBase
    {
        private readonly ILogger<DiagnosticsController> _logger;
        private readonly MqttService _mqttService;

        public DiagnosticsController(ILogger<DiagnosticsController> logger, MqttService mqttService)
        {
            _logger = logger;
            _mqttService = mqttService;
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
    }
}
