using System.Globalization;

namespace AwtrixSharpWeb.Services.TripPlanner
{
    /// <summary>
    /// Transport NSW reads and returns wall-clock times in Sydney, whatever the host's TZ (CR-26).
    /// Cron and Diurnal schedules stay host-local; see readme "Time zones".
    /// </summary>
    public static class TransportTime
    {
        public const string TimeZoneId = "Australia/Sydney";
        private const string WindowsTimeZoneId = "AUS Eastern Standard Time";

        public static TimeZoneInfo Zone { get; } = FindZone();

        private static TimeZoneInfo FindZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                // Windows without ICU (invariant globalization) cannot map IANA ids
                return TimeZoneInfo.FindSystemTimeZoneById(WindowsTimeZoneId);
            }
        }

        /// <summary>The same instant, with Sydney's offset</summary>
        public static DateTimeOffset ToTransportZone(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone);

        /// <summary>The Trip Planner itdDate (yyyyMMdd) and itdTime (HHmm) for an instant</summary>
        public static (string itdDate, string itdTime) ToQuery(DateTimeOffset instant)
        {
            var sydney = ToTransportZone(instant);
            return (sydney.ToString("yyyyMMdd", CultureInfo.InvariantCulture), sydney.ToString("HHmm", CultureInfo.InvariantCulture));
        }

        /// <summary>A Sydney wall-clock time as an instant</summary>
        public static DateTimeOffset FromTransportWallClock(DateTime wallClock)
        {
            var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
            return new DateTimeOffset(unspecified, Zone.GetUtcOffset(unspecified));
        }

        /// <summary>
        /// Parse a user-supplied date/time. Without an offset it is Sydney wall clock; with an offset or Z it is that instant.
        /// </summary>
        public static bool TryParseQuery(string? value, out DateTimeOffset result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(value)
                || !DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return false;
            }

            if (parsed.Kind == DateTimeKind.Unspecified)
            {
                result = FromTransportWallClock(parsed);
                return true;
            }

            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result);
        }

        /// <summary>Parse a Trip Planner timestamp into an instant with Sydney's offset; false when missing or unparsable</summary>
        public static bool TryParseApiTime(string? value, out DateTimeOffset result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(value)
                || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return false;
            }

            result = ToTransportZone(parsed);
            return true;
        }
    }
}
