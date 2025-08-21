using AwtrixSharpWeb.Apps;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Attributes;
using System.ComponentModel;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace AwtrixSharpWeb.Controllers
{
    [DisplayName("Apps")]
    [Route("api/app/[controller]")]
    [ApiController]
    public class TripTimerController : ControllerBase
    {
        Conductor _conductor;

        public TripTimerController(Conductor conductor)
        {
            _conductor = conductor;
        }

        /// <summary>
        /// Start now
        /// </summary>
        [HttpPost("start")]
        public void StartNow([FromQuery] string baseTopic = "awtrix/clock1", [FromQuery] string appName = AppNames.TripTimerApp)
        {
            _conductor.ExecuteNow(baseTopic, appName);
        }


        /// <summary>
        /// Start now
        /// </summary>
        [HttpPost("test/alarm-timings")]
        public IActionResult TestTimingConfig([FromQuery] string departureTime = "2025-09-01 06:41")
        {
            var dateTime = DateTimeOffset.Parse(departureTime);

            var app = _conductor
                            .FindApps(AppNames.TripTimerApp)
                            .First();


            var tripTimer = (TripTimerApp)app;
            var alarmSegments = tripTimer.GetAlarmTime(TripSummary.Factory(dateTime));

            return Ok(alarmSegments);
        }
    }
}
