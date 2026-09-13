using AwtrixSharpWeb.Domain;
using System.Globalization;

namespace AwtrixSharpWeb.Apps.Diurnal
{
    /// <summary>
    /// One validated time-map entry: local time of day and the device settings to apply.
    /// </summary>
    public sealed record DiurnalEntry(TimeSpan Time, AwtrixSettings Settings);

    /// <summary>
    /// The Diurnal time map, parsed and validated once. Pure: no I/O and no clock.
    /// All times are local wall-clock values at minute resolution.
    /// </summary>
    public sealed class DiurnalSchedule
    {
        private const string TimeFormat = "hhmm";
        private const string BrightnessSetting = "brightness";
        private const string GlobalTextColorSetting = "globaltextcolor";

        private readonly List<DiurnalEntry> _entries;

        private DiurnalSchedule(List<DiurnalEntry> entries)
        {
            _entries = entries;
        }

        public static DiurnalSchedule Empty { get; } = new(new List<DiurnalEntry>());

        /// <summary>Entries sorted by time of day.</summary>
        public IReadOnlyList<DiurnalEntry> Entries => _entries;

        public bool IsEmpty => _entries.Count == 0;

        /// <summary>
        /// Parse "HHmm" -> "Name=Value[;Name=Value]" with the invariant culture. Invalid time keys, unknown
        /// setting names and invalid values are logged at Warning and skipped; entries left with no valid
        /// setting are dropped.
        /// </summary>
        public static DiurnalSchedule Parse(IReadOnlyDictionary<string, string>? config, ILogger logger)
        {
            var entries = new List<DiurnalEntry>();
            if (config == null)
            {
                return new DiurnalSchedule(entries);
            }

            foreach (var (key, value) in config)
            {
                var timeKey = key ?? string.Empty;
                if (!TimeSpan.TryParseExact(timeKey.Trim(), TimeFormat, CultureInfo.InvariantCulture, out var time))
                {
                    logger.LogWarning("Diurnal entry '{TimeKey}' ignored: the key must be a 4-digit HHmm time between 0000 and 2359", timeKey);
                    continue;
                }

                var settings = ParseSettings(timeKey, value, logger);
                if (settings.Count == 0)
                {
                    logger.LogWarning("Diurnal entry '{TimeKey}' = '{Value}' ignored: it has no valid settings", timeKey, value);
                    continue;
                }

                entries.Add(new DiurnalEntry(time, settings));
            }

            entries.Sort((a, b) => a.Time.CompareTo(b.Time));
            return new DiurnalSchedule(entries);
        }

        private static AwtrixSettings ParseSettings(string timeKey, string? value, ILogger logger)
        {
            var settings = new AwtrixSettings();
            var parts = (value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var part in parts)
            {
                var pair = part.Split('=', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (pair.Length != 2)
                {
                    logger.LogWarning("Diurnal setting '{Setting}' at {TimeKey} ignored: expected Name=Value", part, timeKey);
                    continue;
                }

                var name = pair[0];
                var settingValue = pair[1];

                switch (name.ToLowerInvariant())
                {
                    case BrightnessSetting:
                        // NumberStyles.Integer matches the pre-WS5 byte.Parse, so "+8" and "-0" stay valid;
                        // "-1" and "300" still fail as overflow and "8.5" as a format error
                        if (byte.TryParse(settingValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var brightness))
                        {
                            settings.SetBrightness(brightness);
                        }
                        else
                        {
                            logger.LogWarning("Diurnal Brightness '{Value}' at {TimeKey} ignored: must be a whole number from 0 to 255", settingValue, timeKey);
                        }
                        break;

                    case GlobalTextColorSetting:
                        settings.SetGlobalTextColor(settingValue);
                        break;

                    default:
                        logger.LogWarning("Diurnal setting '{SettingName}' at {TimeKey} ignored: unknown setting (expected Brightness or GlobalTextColor)", name, timeKey);
                        break;
                }
            }

            return settings;
        }

        /// <summary>
        /// Settings in effect at <paramref name="localNow"/>: all entries replayed chronologically over the
        /// preceding 24 hours (yesterday's entries after now, then today's up to and including now);
        /// the last value per setting wins.
        /// </summary>
        public AwtrixSettings StateAt(DateTime localNow)
        {
            var now = TruncateToMinute(localNow).TimeOfDay;
            var result = new AwtrixSettings();

            foreach (var entry in _entries.Where(e => e.Time > now))
            {
                Merge(result, entry.Settings);
            }

            foreach (var entry in _entries.Where(e => e.Time <= now))
            {
                Merge(result, entry.Settings);
            }

            return result;
        }

        /// <summary>
        /// Settings of every entry occurring in (afterExclusive, upToInclusive], merged chronologically
        /// (last value per setting wins). Empty when the window is empty or runs backwards. A window of a
        /// day or more returns <see cref="StateAt"/> of its end.
        /// </summary>
        public AwtrixSettings DueBetween(DateTime afterExclusive, DateTime upToInclusive)
        {
            var from = TruncateToMinute(afterExclusive);
            var to = TruncateToMinute(upToInclusive);
            var result = new AwtrixSettings();

            if (_entries.Count == 0 || to <= from)
            {
                return result;
            }

            if (to - from >= TimeSpan.FromDays(1))
            {
                return StateAt(to);
            }

            for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
            {
                foreach (var entry in _entries)
                {
                    var occurrence = day + entry.Time;
                    if (occurrence > from && occurrence <= to)
                    {
                        Merge(result, entry.Settings);
                    }
                }
            }

            return result;
        }

        public static DateTime TruncateToMinute(DateTime value)
        {
            return new DateTime(value.Ticks - (value.Ticks % TimeSpan.TicksPerMinute), value.Kind);
        }

        private static void Merge(AwtrixSettings target, AwtrixSettings source)
        {
            foreach (var (key, value) in source)
            {
                target[key] = value;
            }
        }
    }
}
