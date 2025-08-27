using System;
using System.Threading.Tasks;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;
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
        public async Task<IActionResult> FindStops([FromQuery] string query)
        {
            try
            {
                var result = await _tripPlannerService.FindStops(query);
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
        public async Task<IActionResult> GetDepartures([FromQuery] string originId, [FromQuery] string destinationId, [FromQuery] string fromDateTime)
        {
            try
            {
                var fromTimestamp = DateTime.Parse(fromDateTime);
                var result = await _tripPlannerService.GetNextDepartures(originId, destinationId, fromTimestamp);
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
        public async Task<IActionResult> GetTrip([FromQuery] string originId, [FromQuery] string destinationId, [FromQuery] string fromDateTime)
        {
            try
            {
                var fromTimestamp = DateTime.Parse(fromDateTime);
                var result = await _tripPlannerService.GetTrips(originId, destinationId, fromTimestamp);

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