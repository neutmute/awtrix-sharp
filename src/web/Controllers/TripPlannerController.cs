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
        private readonly TripClient _tripClient;
        private readonly ILogger<TripPlannerController> _logger;

        public TripPlannerController(
            StopfinderClient stopFinderClient,
            TripClient tripClient,
            ILogger<TripPlannerController> logger)
        {
            _stopFinderClient = stopFinderClient;
            _tripClient = tripClient;
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

        [HttpGet("trip")]
        public async Task<IActionResult> GetTrip([FromQuery] string originId, [FromQuery] string destinationId)
        {
            try
            {
                // Use the TripClient to get trip information
                // Setting up default parameters based on the API requirements
                var result = await _tripClient.Request2Async(
                    outputFormat: OutputFormat5.RapidJSON,
                    coordOutputFormat: CoordOutputFormat4.EPSG4326,
                    depArrMacro: DepArrMacro.Dep, // Departing after the specified time
                    itdDate: DateTime.Now.ToString("yyyyMMdd"), // Today's date
                    itdTime: DateTime.Now.ToString("HHmm"), // Current time
                    type_origin: Type_origin.Any,
                    name_origin: originId,
                    type_destination: Type_destination.Any,
                    name_destination: destinationId,
                    calcNumberOfTrips: 3, // Return 3 trip options
                    wheelchair: null,
                    excludedMeans: null,
                    exclMOT_1: null,
                    exclMOT_2: null,
                    exclMOT_4: null,
                    exclMOT_5: null,
                    exclMOT_7: null,
                    exclMOT_9: null,
                    exclMOT_11: null,
                    tfNSWTR: TfNSWTR.True, // Enable real-time data
                    version: null,
                    itOptionsActive: null,
                    computeMonomodalTripBicycle: null,
                    cycleSpeed: null,
                    bikeProfSpeed: null,
                    maxTimeBicycle: null,
                    onlyITBicycle: null,
                    useElevationData: null,
                    elevFac: null);
                
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