using AwtrixSharpWeb.Controllers;
using TransportOpenData.TripPlanner;

namespace AwtrixSharpWeb.Services
{
    public class TripPlannerService
    {
        private readonly StopfinderClient _stopFinderClient;
        private readonly TripClient _tripClient;
        private readonly ILogger<TripPlannerService> _logger; 
        
        public TripPlannerService(
            StopfinderClient stopFinderClient,
            TripClient tripClient,
            ILogger<TripPlannerService> logger)
        {
            _stopFinderClient = stopFinderClient;
            _tripClient = tripClient;
            _logger = logger;

        }

        public async Task<StopFinderResponse> FindStops(string query)
        {
            return await _stopFinderClient.RequestAsync(OutputFormat4.RapidJSON, Type_sf.Stop, query, CoordOutputFormat3.EPSG4326, TfNSWSF.True, null);
        }

        public async Task<TripRequestResponse> GetTrip(string originStopId, string destinationStopId)
        {
            var now = DateTime.Now;

            // Use the TripClient to get trip information
            // Setting up default parameters based on the API requirements
            var result = await _tripClient.Request2Async(
                outputFormat: OutputFormat5.RapidJSON,
                coordOutputFormat: CoordOutputFormat4.EPSG4326,
                depArrMacro: DepArrMacro.Dep, // Departing after the specified time
                itdDate: now.ToString("yyyyMMdd"), // Today's date
                itdTime: now.ToString("HHmm"), // Current time
                type_origin: Type_origin.Any,
                name_origin: originStopId,
                type_destination: Type_destination.Any,
                name_destination: destinationStopId,
                calcNumberOfTrips: 5, 
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

            return result;
        }

        public async Task<List<DateTimeOffset>> GetNextDepartures(string originStopId, string destinationStopId)
        {
            var trips = await GetTrip(originStopId, destinationStopId);

            var output = new List<DateTimeOffset>();
            foreach(var journey in trips.Journeys)
            {
                var departString = journey.Legs.First().Origin.DepartureTimeEstimated;
                var utcTime = DateTimeOffset.Parse(departString);
                var localTime = TimeZoneInfo.ConvertTime(utcTime, TimeZoneInfo.Local);
                output.Add(localTime);
            }

            return output;
        }   
    }
}
