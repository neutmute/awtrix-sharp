using AwtrixSharpWeb.Apps.TripTimer;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.Globalization;


namespace AwtrixSharpWeb.Controllers
{
    [SwaggerTag("Apps")]
    [Route("api/app/[controller]")]
    [ApiController]
    public class TripTimerController : ControllerBase
    {
        private readonly Conductor _conductor;

        public TripTimerController(Conductor conductor)
        {
            _conductor = conductor;
        }

        /// <summary>
        /// Start now
        /// </summary>
        [HttpPost("start")]
        [SwaggerResponse(200, "App started successfully")]
        [SwaggerResponse(404, "The app is not running on that device")]
        [SwaggerResponse(500, "The app failed to start")]
        public IActionResult StartNow([FromQuery] string baseTopic = "awtrix/clock1", [FromQuery] string appName = AppNames.TripTimerApp)
        {
            var result = _conductor.ExecuteNow(baseTopic, appName);
            return this.ToActionResult(result, appName, baseTopic);
        }


        [HttpPost("test/alarm-timings")]
        public IActionResult TestTimingConfig([FromQuery] string departureTime = "2025-09-01 06:41")
        {
            // CR-15: invariant culture, 400 instead of an unhandled FormatException
            if (!DateTimeOffset.TryParse(departureTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dateTime))
            {
                return BadRequest(new { message = $"departureTime '{departureTime}' is not a valid date/time; use yyyy-MM-dd HH:mm" });
            }

            var tripTimer = _conductor
                            .FindApps(AppNames.TripTimerApp)
                            .OfType<TripTimerApp>()
                            .FirstOrDefault();

            if (tripTimer == null)
            {
                return NotFound(new { message = "No TripTimerApp is running" });
            }

            var alarmSegments = tripTimer.GetAlarmTime(TripSummary.Factory(dateTime));

            return Ok(alarmSegments);
        }
    }
}
