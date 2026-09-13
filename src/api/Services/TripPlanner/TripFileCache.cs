using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AwtrixSharpWeb.Services.TripPlanner
{
    /// <summary>
    /// Optional departures cache for slow Transport NSW connections: {directory}/trip_{origin}_{destination}_{HH}.json holds a
    /// serialised List&lt;TripSummary&gt;, where HH is the Sydney hour of the query. Only wall-clock times are used; they are
    /// re-dated onto the query's Sydney date. Any problem returns null so the caller uses the live API (CR-37).
    /// </summary>
    internal sealed class TripFileCache
    {
        public const string DataDirectoryVariable = "AWTRIXSHARP_SETTINGS__DATA_DIRECTORY";

        /// <summary>A cached departure more than this far before the query time is taken to be after midnight</summary>
        internal static readonly TimeSpan RolloverTolerance = TimeSpan.FromHours(1);

        private static readonly Regex SafeStopId = new(@"^[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant);

        private readonly string? _directory;
        private readonly ILogger _logger;

        public TripFileCache(string? directory, ILogger logger)
        {
            _directory = directory;
            _logger = logger;
        }

        public async Task<List<TripSummary>?> TryLoadAsync(string originStopId, string destinationStopId, DateTimeOffset fromWhen, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_directory))
            {
                return null;
            }

            if (!SafeStopId.IsMatch(originStopId ?? string.Empty) || !SafeStopId.IsMatch(destinationStopId ?? string.Empty))
            {
                _logger.LogWarning("Trip cache not used: stop ids {Origin}/{Destination} may only contain letters, digits, '-' and '_'",
                    originStopId, destinationStopId);
                return null;
            }

            var queryTime = TransportTime.ToTransportZone(fromWhen);
            var fileName = string.Create(CultureInfo.InvariantCulture, $"trip_{originStopId}_{destinationStopId}_{queryTime:HH}.json");
            var path = Path.Combine(_directory, fileName);

            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                _logger.LogInformation("Loading trip data from {CacheFile}", path);

                List<TripSummary?>? cached;
                await using (var stream = File.OpenRead(path))
                {
                    cached = await JsonSerializer.DeserializeAsync<List<TripSummary?>>(stream, cancellationToken: cancellationToken);
                }

                var trips = cached?
                    .Where(trip => trip?.Origin != null && trip.Destination != null)
                    .Select(trip => Redate(trip!, queryTime))
                    .ToList();

                if (trips is not { Count: > 0 })
                {
                    _logger.LogWarning("Trip cache file {CacheFile} holds no trips; using the API", path);
                    return null;
                }

                return trips;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                _logger.LogWarning(ex, "Trip cache file {CacheFile} could not be read; using the API", path);
                return null;
            }
        }

        /// <summary>
        /// Put a cached trip's Sydney wall-clock times onto <paramref name="queryTime"/>'s date, rolling to the next day
        /// when the departure would be more than <see cref="RolloverTolerance"/> before the query.
        /// </summary>
        internal static TripSummary Redate(TripSummary trip, DateTimeOffset queryTime)
        {
            var queryDate = queryTime.DateTime.Date;
            var departTimeOfDay = trip.Origin.Time.TimeOfDay;

            var departs = TransportTime.FromTransportWallClock(queryDate + departTimeOfDay);
            if (departs < queryTime - RolloverTolerance)
            {
                departs = TransportTime.FromTransportWallClock(queryDate.AddDays(1) + departTimeOfDay);
            }

            var travel = trip.Destination.Time.TimeOfDay - departTimeOfDay;
            if (travel < TimeSpan.Zero)
            {
                travel += TimeSpan.FromDays(1);
            }

            return new TripSummary
            {
                Origin = TimePlace.Factory(departs, trip.Origin.Place),
                Destination = TimePlace.Factory(TransportTime.ToTransportZone(departs + travel), trip.Destination.Place)
            };
        }
    }
}
