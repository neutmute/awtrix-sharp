using System;
using System.Threading.Tasks;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TransportOpenData.TripPlanner;

namespace AwtrixSharpWeb.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
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
        public async Task<IActionResult> GetDepartures([FromQuery] string originId, [FromQuery] string destinationId)
        {
            try
            {
                var result = await _tripPlannerService.GetNextDepartures(originId, destinationId);
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
        public async Task<IActionResult> GetTrip([FromQuery] string originId, [FromQuery] string destinationId)
        {
            try
            {
                // Use the TripClient to get trip information
                // Setting up default parameters based on the API requirements
                var result = await _tripPlannerService.GetTrip(originId, destinationId);

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