using AwtrixSharpWeb.HostedServices;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace AwtrixSharpWeb.Controllers
{

    [SwaggerTag("Apps")]
    [Route("api/app/[controller]")]
    [ApiController]
    public class MqttRenderController : ControllerBase
    {
        private readonly Conductor _conductor;

        public MqttRenderController(Conductor conductor)
        {
            _conductor = conductor;
        }

        /// <summary>
        /// Triggers an app to execute immediately
        /// </summary>
        /// <param name="deviceAddress">The Awtrix device address (topic)</param>
        /// <param name="appName">The name of the app to execute</param>
        [HttpPost("start")]
        [SwaggerOperation(
            Summary = "Start an app immediately",
            Description = "Triggers the specified app to execute immediately on the specified Awtrix device",
            OperationId = "StartApp"
        )]
        [SwaggerResponse(200, "App started successfully")]
        [SwaggerResponse(404, "The app is not running on that device")]
        [SwaggerResponse(500, "The app failed to start")]
        public IActionResult StartNow(
            [FromQuery, SwaggerParameter("The Awtrix device address/topic")]
            string deviceAddress = "awtrix/clock1",

            [FromQuery, SwaggerParameter("The name of the app to execute")]
            string appName = AppNames.MqttRenderApp)
        {
            var result = _conductor.ExecuteNow(deviceAddress, appName);
            return this.ToActionResult(result, appName, deviceAddress);
        }
    }
}
