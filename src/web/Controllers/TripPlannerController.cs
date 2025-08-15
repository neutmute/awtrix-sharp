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
        private readonly StopfinderClient _stopFinderClient;
        private readonly ILogger<TripPlannerController> _logger;

        public TripPlannerController(
            StopfinderClient stopFinderClient,
            ILogger<TripPlannerController> logger)
        {
            _stopFinderClient = stopFinderClient;
            _logger = logger;
        }

        [HttpGet("stops")]
        public async Task<IActionResult> FindStops([FromQuery] string query)
        {
            try
            {
                var result = await _stopFinderClient.RequestAsync(OutputFormat4.RapidJSON, Type_sf.Stop, query, CoordOutputFormat3.EPSG4326, TfNSWSF.True, null);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding stops");
                return StatusCode(500, "An error occurred while finding stops");
            }
        }

        //[HttpGet("departures")]
        //public async Task<IActionResult> GetDepartures([FromQuery] string stopId)
        //{
        //    try
        //    {
        //        var result = await _stopFinderClient.GetDeparturesAsync(stopId);
        //        return Ok(result);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error getting departures");
        //        return StatusCode(500, "An error occurred while getting departures");
        //    }
        //}

        //[HttpGet("trip")]
        //public async Task<IActionResult> GetTrip([FromQuery] string originId, [FromQuery] string destinationId)
        //{
        //    try
        //    {
        //        var result = await _stopFinderClient.GetTripAsync(originId, destinationId);
        //        return Ok(result);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error getting trip information");
        //        return StatusCode(500, "An error occurred while getting trip information");
        //    }
        //}
    }
}