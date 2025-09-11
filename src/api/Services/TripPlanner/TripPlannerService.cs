using AwtrixSharpWeb.Controllers;
using AwtrixSharpWeb.Interfaces;
using TransportOpenData.TripPlanner;

namespace AwtrixSharpWeb.Services.TripPlanner
{

    public class TripPlannerService : ITripPlannerService
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
            return await _stopFinderClient.RequestAsync(
                            OutputFormat4.RapidJSON
                            , Type_sf.Any
                            , query
                            , CoordOutputFormat3.EPSG4326
                            , null
                            , null);
        }

        public async Task<TripRequestResponse> GetTrips(string originStopId, string destinationStopId, DateTime fromWhen)
        {
            _logger.LogInformation("Getting trip from {Origin} to {Destination} from {Time:HH:mm}", originStopId, destinationStopId, fromWhen);

            // Use the TripClient to get trip information
            // Setting up default parameters based on the API requirements
            var result = await _tripClient.Request2Async(
                outputFormat: OutputFormat5.RapidJSON,
                coordOutputFormat: CoordOutputFormat4.EPSG4326,
                depArrMacro: DepArrMacro.Dep, // Departing after the specified time
                itdDate: fromWhen.ToString("yyyyMMdd"), // Today's date
                itdTime: fromWhen.ToString("HHmm"), // Current time
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

        public async Task<List<TripSummary>> GetNextDepartures(string originStopId, string destinationStopId, DateTime fromWhen)
        {
            var trips = await GetTrips(originStopId, destinationStopId, fromWhen);

            var output = new List<TripSummary>();


            DateTimeOffset ParseTime(string s)
            {
                var datetime = DateTimeOffset.Parse(s);
                return TimeZoneInfo.ConvertTime(datetime, TimeZoneInfo.Local);
            }

            foreach (var journey in trips.Journeys)
            {
                var firstLeg = journey.Legs.First();
                var lastLeg = journey.Legs.Last();

                var origin = firstLeg.Origin;
                var destination = lastLeg.Destination;

                var departs = ParseTime(origin.DepartureTimeEstimated);
                var arrives = ParseTime(destination.ArrivalTimeEstimated);

                var summary = new TripSummary
                {
                    Origin = TimePlace.Factory(departs, origin.DisassembledName) ,
                    Destination = TimePlace.Factory(arrives, destination.DisassembledName)
                };
                output.Add(summary);
            }

            return output;
        }
    }
}
