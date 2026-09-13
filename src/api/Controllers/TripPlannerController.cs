using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;
using System;
using System.Threading;
using System.Threading.Tasks;
using TransportOpenData.TripPlanner;

namespace AwtrixSharpWeb.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [SwaggerTag("Services")]
    public class TripPlannerController : ControllerBase
    {
        private readonly TripPlannerService _tripPlannerService;
        private readonly ILogger<TripPlannerController> _logger;

        public TripPlannerController(
            TripPlannerService tripPlannerService,
            ILogger<TripPlannerController> logger)
        {
            _tripPlannerService = tripPlannerService;
            _logger = logger;
        }

        [HttpGet("stops")]
        [SwaggerOperation(Summary = "Find stops matching a query")]
        public async Task<IActionResult> FindStops([FromQuery] string query, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await _tripPlannerService.FindStops(query, cancellationToken);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding stops");
                return StatusCode(500, "An error occurred while finding stops");
            }
        }

        [HttpGet("departures")]
        [SwaggerOperation(Summary = "Get upcoming departures between stops")]
        public async Task<IActionResult> GetDepartures([FromQuery] string originId, [FromQuery] string destinationId, [FromQuery] string fromDateTime, CancellationToken cancellationToken = default)
        {
            // An offset-less value is Sydney wall clock, whatever the host TZ (CR-26); invariant culture, 400 when unparseable (CR-15)
            if (!TransportTime.TryParseQuery(fromDateTime, out var fromTimestamp))
            {
                return BadRequest(new { message = $"fromDateTime '{fromDateTime}' is not a valid date/time; use yyyy-MM-ddTHH:mm" });
            }

            try
            {
                var result = await _tripPlannerService.GetNextDepartures(originId, destinationId, fromTimestamp, cancellationToken);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting departures");
                return StatusCode(500, "An error occurred while getting departures");
            }
        }


        /// <summary>
        /// Very detailed trip data
        /// </summary>
        [HttpGet("trip")]
        [SwaggerOperation(Summary = "Get detailed trip information")]
        public async Task<IActionResult> GetTrip([FromQuery] string originId, [FromQuery] string destinationId, [FromQuery] string fromDateTime, CancellationToken cancellationToken = default)
        {
            // An offset-less value is Sydney wall clock, whatever the host TZ (CR-26); invariant culture, 400 when unparseable (CR-15)
            if (!TransportTime.TryParseQuery(fromDateTime, out var fromTimestamp))
            {
                return BadRequest(new { message = $"fromDateTime '{fromDateTime}' is not a valid date/time; use yyyy-MM-ddTHH:mm" });
            }

            try
            {
                var result = await _tripPlannerService.GetTrips(originId, destinationId, fromTimestamp, cancellationToken);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting trip information");
                return StatusCode(500, "An error occurred while getting trip information");
            }
        }
    }
}