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
        private readonly TripPlannerClient _tripPlannerClient;
        private readonly ILogger<TripPlannerController> _logger;

        public TripPlannerController(
            TripPlannerClient tripPlannerClient,
            ILogger<TripPlannerController> logger)
        {
            _tripPlannerClient = tripPlannerClient;
            _logger = logger;
        }

        [HttpGet("stops")]
        public async Task<IActionResult> FindStops([FromQuery] string query)
        {
            try
            {
                var result = await _tripPlannerClient.FindStopsAsync(query);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding stops");
                return StatusCode(500, "An error occurred while finding stops");
            }
        }

        [HttpGet("departures")]
        public async Task<IActionResult> GetDepartures([FromQuery] string stopId)
        {
            try
            {
                var result = await _tripPlannerClient.GetDeparturesAsync(stopId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting departures");
                return StatusCode(500, "An error occurred while getting departures");
            }
        }

        [HttpGet("trip")]
        public async Task<IActionResult> GetTrip([FromQuery] string originId, [FromQuery] string destinationId)
        {
            try
            {
                var result = await _tripPlannerClient.GetTripAsync(originId, destinationId);
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