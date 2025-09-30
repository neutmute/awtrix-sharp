using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Controllers;
using AwtrixSharpWeb.Interfaces;
using Newtonsoft.Json.Linq;
using System.Text.Json;
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
            var cachedDepatures = await TryLocalCache(originStopId, destinationStopId, fromWhen);
            if (cachedDepatures?.Count > 0)
            {
                _logger.LogInformation("Using {tripCount} cached trip entries", cachedDepatures.Count);
                return cachedDepatures;
            }

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
                    Origin = TimePlace.Factory(departs, origin.DisassembledName),
                    Destination = TimePlace.Factory(arrives, destination.DisassembledName)
                };
                output.Add(summary);
            }

            return output;
        }

        private async Task<List<TripSummary>> TryLocalCache(string originStopId, string destinationStopId, DateTime fromWhen)
        {
            // Transport NSW data connection times aren't great, so allow override via file cache
            // See TripPlannerController::GetDepartures to generate cache files 
            var cacheFolder = Environment.GetEnvironmentVariable("AWTRIXSHARP_SETTINGS__DATA_DIRECTORY");

            if (!string.IsNullOrEmpty(cacheFolder))
            {
                var cacheFilename = $"trip_{originStopId}_{destinationStopId}_{fromWhen:HH}.json";
                var fullCachePath = Path.Combine(cacheFolder, cacheFilename);
                if (File.Exists(fullCachePath))
                {
                    _logger.LogInformation("Loading trip data from {CacheFile}", fullCachePath);
                    var cachedJson = await File.ReadAllTextAsync(fullCachePath);
                    var cachedTrips = JsonSerializer.Deserialize<List<TripSummary>>(cachedJson);

                    var now = DateTimeOffset.Now;
                    foreach(var trip in cachedTrips)
                    {   
                        // Adjust times to be today
                        trip.Origin.Time = new DateTimeOffset(
                            now.Year,
                            now.Month,
                            now.Day,
                            trip.Origin.Time.Hour,
                            trip.Origin.Time.Minute,
                            trip.Origin.Time.Second,
                            now.Offset);

                        trip.Destination.Time = new DateTimeOffset(
                            now.Year,
                            now.Month,
                            now.Day,
                            trip.Destination.Time.Hour,
                            trip.Destination.Time.Minute,
                            trip.Destination.Time.Second,
                            now.Offset);
                    }

                    return cachedTrips;
                }
            }

            return new List<TripSummary>();
        }
    }
}
