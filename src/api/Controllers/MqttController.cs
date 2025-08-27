using AwtrixSharpWeb.HostedServices;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace AwtrixSharpWeb.Controllers
{
    [SwaggerTag("Services")]
    [ApiController]
    [Route("[controller]")]
    public class MqttController : ControllerBase
    {
        private readonly MqttConnector _mqttService;

        public MqttController(MqttConnector mqttService)
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