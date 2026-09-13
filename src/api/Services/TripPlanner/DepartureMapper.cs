using TransportOpenData.TripPlanner;

namespace AwtrixSharpWeb.Services.TripPlanner
{
    /// <summary>
    /// Turns a Trip Planner response into departures from the first boarded service. Never throws: an error body,
    /// a journey with no legs or times, or one malformed journey is logged and skipped without losing the others (CR-10).
    /// </summary>
    internal static class DepartureMapper
    {
        /// <summary>TfNSW product classes for walking legs: 99 footpath, 100 walk between stops (CR-27)</summary>
        private static readonly int[] FootpathProductClasses = { 99, 100 };

        public static List<TripSummary> Map(TripRequestResponse? response, ILogger logger)
        {
            var output = new List<TripSummary>();

            if (response == null)
            {
                logger.LogWarning("Trip planner returned an empty body");
                return output;
            }

            if (response.Error != null)
            {
                logger.LogWarning("Trip planner returned an error: {Message}", response.Error.Message);
            }

            if (response.Journeys == null)
            {
                return output;
            }

            var seenDepartures = new HashSet<DateTimeOffset>();
            var index = 0;

            foreach (var journey in response.Journeys)
            {
                index++;
                try
                {
                    var summary = MapJourney(journey, index, logger);
                    if (summary == null)
                    {
                        continue;
                    }

                    if (!seenDepartures.Add(summary.Origin.Time))
                    {
                        logger.LogDebug("Journey {Index} duplicates the {Departure:HH:mm:ss} departure; skipped", index, summary.Origin.Time);
                        continue;
                    }

                    output.Add(summary);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Journey {Index} could not be read; skipped", index);
                }
            }

            return output;
        }

        internal static bool IsFootpath(TripRequestResponseJourneyLeg leg) =>
            leg.Transportation?.Product?.Class is int productClass && FootpathProductClasses.Contains(productClass);

        /// <summary>
        /// CR-28: TfNSW's exact cancellation value is unconfirmed (repo fixtures only show MONITORED), so any realtime
        /// status containing "CANCEL" counts (CANCELLED, TRIP_CANCELLED, ...).
        /// </summary>
        internal static bool IsCancelled(TripRequestResponseJourneyLeg leg) =>
            leg.RealtimeStatus?.Any(status => status != null && status.Contains("CANCEL", StringComparison.OrdinalIgnoreCase)) == true;

        private static TripSummary? MapJourney(TripRequestResponseJourney? journey, int index, ILogger logger)
        {
            var legs = journey?.Legs?.Where(leg => leg != null).ToList() ?? new List<TripRequestResponseJourneyLeg>();
            if (legs.Count == 0)
            {
                logger.LogWarning("Journey {Index} has no legs; skipped", index);
                return null;
            }

            var transitLegs = legs.Where(leg => !IsFootpath(leg)).ToList();
            if (transitLegs.Count == 0)
            {
                logger.LogDebug("Journey {Index} is walking only; skipped", index);
                return null;
            }

            if (transitLegs.Any(IsCancelled))
            {
                logger.LogInformation("Journey {Index} includes a cancelled service; skipped", index);
                return null;
            }

            var boarding = transitLegs[0];
            var final = legs[^1];

            var firstStop = boarding.StopSequence?.FirstOrDefault();
            if (!FirstTime(out var departs,
                    boarding.Origin?.DepartureTimeEstimated,
                    boarding.Origin?.DepartureTimePlanned,
                    firstStop?.DepartureTimeEstimated,
                    firstStop?.DepartureTimePlanned))
            {
                logger.LogWarning("Journey {Index} has no departure time; skipped", index);
                return null;
            }

            var lastStop = final.StopSequence?.LastOrDefault();
            if (!FirstTime(out var arrives,
                    final.Destination?.ArrivalTimeEstimated,
                    final.Destination?.ArrivalTimePlanned,
                    lastStop?.ArrivalTimeEstimated,
                    lastStop?.ArrivalTimePlanned))
            {
                arrives = departs; // only the departure drives the alarm
            }

            return new TripSummary
            {
                Origin = TimePlace.Factory(departs, PlaceName(boarding.Origin)),
                Destination = TimePlace.Factory(arrives, PlaceName(final.Destination))
            };
        }

        /// <summary>Estimated before planned: TfNSW's estimated time is the realtime value</summary>
        private static bool FirstTime(out DateTimeOffset time, params string?[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (TransportTime.TryParseApiTime(candidate, out time))
                {
                    return true;
                }
            }

            time = default;
            return false;
        }

        private static string PlaceName(TripRequestResponseJourneyLegStop? stop) => stop?.DisassembledName ?? stop?.Name ?? string.Empty;
    }
}
