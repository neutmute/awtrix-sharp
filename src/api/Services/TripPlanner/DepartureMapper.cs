using TransportOpenData.TripPlanner;

namespace AwtrixSharpWeb.Services.TripPlanner
{
    /// <summary>
    /// Turns a Trip Planner response into departures from the configured origin (first leg, including a leading walk). Never throws: an error body,
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

            // CR-27 (C1 ruling): journeys that board the same transit service are duplicates. Keep the one whose first leg
            // departs latest (least waiting), in the position of the first one seen.
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            var index = 0;

            foreach (var journey in response.Journeys)
            {
                index++;
                try
                {
                    var mapped = MapJourney(journey, index, logger);
                    if (mapped == null)
                    {
                        continue;
                    }

                    var (summary, serviceKey) = mapped.Value;
                    if (positions.TryGetValue(serviceKey, out var position))
                    {
                        var kept = output[position];
                        if (summary.Origin.Time > kept.Origin.Time)
                        {
                            output[position] = summary;
                        }

                        logger.LogDebug("Journey {Index} boards the same service as another journey ({Service}); keeping the {Departure:HH:mm:ss} departure",
                            index, serviceKey, output[position].Origin.Time);
                        continue;
                    }

                    positions[serviceKey] = output.Count;
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

        /// <summary>
        /// The trip timer answers "when must I leave the configured origin" (C1 ruling). The departure is the FIRST leg's
        /// departure, so a leading walk from the origin counts from the walk start; the place is where the first transit
        /// leg is boarded. The service key identifies that boarded service: the model carries no trip id, so it is the
        /// boarding stop plus the transit departure instant.
        /// </summary>
        private static (TripSummary Summary, string ServiceKey)? MapJourney(TripRequestResponseJourney? journey, int index, ILogger logger)
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

            var first = legs[0];
            var boarding = transitLegs[0];
            var final = legs[^1];

            if (!TryDeparture(first, out var departs))
            {
                logger.LogWarning("Journey {Index} has no departure time; skipped", index);
                return null;
            }

            var boardingDeparts = ReferenceEquals(first, boarding) ? departs
                : TryDeparture(boarding, out var transitDeparts) ? transitDeparts
                : (DateTimeOffset?)null;

            var lastStop = final.StopSequence?.LastOrDefault();
            if (!FirstTime(out var arrives,
                    final.Destination?.ArrivalTimeEstimated,
                    final.Destination?.ArrivalTimePlanned,
                    lastStop?.ArrivalTimeEstimated,
                    lastStop?.ArrivalTimePlanned))
            {
                arrives = departs; // only the departure drives the alarm
            }

            var boardingPlace = PlaceName(boarding.Origin);
            var boardingStop = boarding.Origin?.Id ?? boardingPlace;
            var serviceKey = boardingDeparts is { } at
                ? $"{boardingStop}|{at.UtcDateTime:O}"
                : $"{boardingStop}|journey {index}"; // no transit time: nothing to match on, never a duplicate

            var summary = new TripSummary
            {
                Origin = TimePlace.Factory(departs, boardingPlace),
                Destination = TimePlace.Factory(arrives, PlaceName(final.Destination))
            };
            return (summary, serviceKey);
        }

        /// <summary>Origin Estimated → Planned → stopSequence[0] Estimated → Planned; unparsable values fall through</summary>
        private static bool TryDeparture(TripRequestResponseJourneyLeg leg, out DateTimeOffset departs)
        {
            var firstStop = leg.StopSequence?.FirstOrDefault();
            return FirstTime(out departs,
                leg.Origin?.DepartureTimeEstimated,
                leg.Origin?.DepartureTimePlanned,
                firstStop?.DepartureTimeEstimated,
                firstStop?.DepartureTimePlanned);
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
