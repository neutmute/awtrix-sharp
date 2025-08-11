using Microsoft.AspNetCore.Mvc;
using AwtrixSharpWeb.Services;

namespace AwtrixSharpWeb.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class MqttController : ControllerBase
    {
        private readonly MqttService _mqttService;

        public MqttController(MqttService mqttService)
        {
            _mqttService = mqttService;
        }

        [HttpPost("publish")]
        public async Task<IActionResult> Publish(string topic, string payload)
        {
            await _mqttService.ConnectAsync();
            await _mqttService.PublishAsync(topic, payload);
            return Ok("Message published");
        }
    }
}