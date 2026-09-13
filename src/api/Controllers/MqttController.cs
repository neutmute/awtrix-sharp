using AwtrixSharpWeb.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace AwtrixSharpWeb.Controllers
{
    [SwaggerTag("Services")]
    [ApiController]
    [Route("[controller]")]
    public class MqttController : ControllerBase
    {
        private readonly IMqttConnector _mqttConnector;

        public MqttController(IMqttConnector mqttConnector)
        {
            _mqttConnector = mqttConnector;
        }

        /// <summary>
        /// Publish a raw MQTT message. The connector owns the connection; this endpoint never
        /// connects or reconnects (CR-04).
        /// </summary>
        [HttpPost("publish")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Publish(string topic, string payload)
        {
            if (await _mqttConnector.PublishAsync(topic, payload))
            {
                return Ok("Message published");
            }

            return StatusCode(StatusCodes.Status503ServiceUnavailable, "MQTT publish failed: broker not connected or publish rejected");
        }
    }
}
