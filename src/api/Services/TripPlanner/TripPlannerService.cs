using AwtrixSharpWeb.Interfaces;
using Microsoft.Extensions.Options;
using TransportOpenData;
using TransportOpenData.TripPlanner;

namespace AwtrixSharpWeb.Services.TripPlanner
{
    /// <summary>
    /// Transport NSW Trip Planner access. A singleton: every call builds a short-lived NSwag client over the named
    /// IHttpClientFactory client, so pooled handlers rotate (DNS changes are picked up), requests time out after
    /// <see cref="HttpTimeout"/>, and callers can cancel (CR-29). BaseUrl comes from TransportOpenDataConfig (CR-36).
    /// Times on the wire are Sydney wall clock (CR-26).
    /// </summary>
    public class TripPlannerService : ITripPlannerService
    {
        public const string HttpClientName = "TransportOpenData";
        public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(15);
        internal const int TripsPerQuery = 5;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptions<TransportOpenDataConfig> _config;
        private readonly ILogger<TripPlannerService> _logger;

        public TripPlannerService(
            IHttpClientFactory httpClientFactory,
            IOptions<TransportOpenDataConfig> config,
            ILogger<TripPlannerService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
            _logger = logger;
        }

        public async Task<StopFinderResponse> FindStops(string query, CancellationToken cancellationToken = default)
        {
            using var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            var client = new StopfinderClient(httpClient);
            ApplyBaseUrl(url => client.BaseUrl = url);

            return await client.RequestAsync(
                OutputFormat4.RapidJSON,
                Type_sf.Any,
                query,
                CoordOutputFormat3.EPSG4326,
                null,
                null,
                cancellationToken);
        }

        public async Task<TripRequestResponse> GetTrips(string originStopId, string destinationStopId, DateTimeOffset fromWhen, CancellationToken cancellationToken = default)
        {
            var (itdDate, itdTime) = TransportTime.ToQuery(fromWhen);
            _logger.LogInformation("Getting trips from {Origin} to {Destination} departing after {ItdDate} {ItdTime} ({TimeZone})",
                originStopId, destinationStopId, itdDate, itdTime, TransportTime.TimeZoneId);

            using var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            var client = new TripClient(httpClient);
            ApplyBaseUrl(url => client.BaseUrl = url);

            return await client.Request2Async(
                outputFormat: OutputFormat5.RapidJSON,
                coordOutputFormat: CoordOutputFormat4.EPSG4326,
                depArrMacro: DepArrMacro.Dep, // Departing after the specified time
                itdDate: itdDate,
                itdTime: itdTime,
                type_origin: Type_origin.Any,
                name_origin: originStopId,
                type_destination: Type_destination.Any,
                name_destination: destinationStopId,
                calcNumberOfTrips: TripsPerQuery,
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
                elevFac: null,
                cancellationToken: cancellationToken);
        }

        public async Task<List<TripSummary>> GetNextDepartures(string originStopId, string destinationStopId, DateTimeOffset fromWhen, CancellationToken cancellationToken = default)
        {
            // Opt-in file cache; any problem with it falls back to the API (CR-37)
            var cache = new TripFileCache(Environment.GetEnvironmentVariable(TripFileCache.DataDirectoryVariable), _logger);
            var cachedDepartures = await cache.TryLoadAsync(originStopId, destinationStopId, fromWhen, cancellationToken);
            if (cachedDepartures != null)
            {
                _logger.LogInformation("Using {TripCount} cached trip entries", cachedDepartures.Count);
                return cachedDepartures;
            }

            var trips = await GetTrips(originStopId, destinationStopId, fromWhen, cancellationToken);
            return DepartureMapper.Map(trips, _logger);
        }

        private void ApplyBaseUrl(Action<string> apply)
        {
            // CR-36: honour TransportOpenData:BaseUrl; blank keeps the generated default
            var baseUrl = _config.Value.BaseUrl;
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                apply(baseUrl);
            }
        }
    }
}
