using AwtrixSharpWeb.Domain;
using System.Globalization;

namespace AwtrixSharpWeb.Apps.Configs
{
    /// <summary>
    /// Static map from ValueMap key (case-insensitive, the NG payload name) to the <see cref="AwtrixAppMessage"/>
    /// setter it drives. Values are parsed with the invariant culture. A test asserts every public
    /// single-argument Set* method (except the TimeSpan conveniences) has an entry.
    /// </summary>
    internal static class ValueMapSetters
    {
        private static readonly Dictionary<string, Func<AwtrixAppMessage, string, bool>> Setters =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Text"] = Str((m, v) => m.SetText(v)),
                ["TextCase"] = EnumName<TextCase>((m, v) => m.SetTextCase(v)),
                ["Hold"] = Bool((m, v) => m.SetHold(v)),
                ["Stack"] = Bool((m, v) => m.SetStack(v)),
                ["TextOffsetX"] = Int((m, v) => m.SetTextOffsetX(v)),
                ["TextCenter"] = Bool((m, v) => m.SetTextCenter(v)),
                ["TextColor"] = Colour((m, v) => m.SetTextColor(v)),
                ["BackgroundColor"] = Colour((m, v) => m.SetBackgroundColor(v)),
                ["Palette"] = Palette,
                ["PaletteBlend"] = Bool((m, v) => m.SetPaletteBlend(v)),
                ["TextBlinkMs"] = Int((m, v) => m.SetTextBlinkMs(v)),
                ["TextFadeMs"] = Int((m, v) => m.SetTextFadeMs(v)),
                ["Icon"] = Str((m, v) => m.SetIcon(v)),
                ["IconMode"] = EnumName<IconMode>((m, v) => m.SetIconMode(v)),
                ["DurationMs"] = Int((m, v) => m.SetDurationMs(v)),
                ["LifetimeMs"] = Int((m, v) => m.SetLifetimeMs(v)),
                ["LifetimeExpiry"] = EnumName<LifetimeExpiry>((m, v) => m.SetLifetimeExpiry(v)),
                ["LineChart"] = IntArray((m, v) => m.SetLineChart(v)),
                ["BarChart"] = IntArray((m, v) => m.SetBarChart(v)),
                ["ChartAutoscale"] = Bool((m, v) => m.SetChartAutoscale(v)),
                ["Overlay"] = Str((m, v) => m.SetOverlay(v)),
                ["Progress"] = Int((m, v) => m.SetProgress(v)),
                ["ProgressColor"] = Colour((m, v) => m.SetProgressColor(v)),
                ["ProgressTrackColor"] = Colour((m, v) => m.SetProgressTrackColor(v)),
                ["ScrollSpeed"] = Int((m, v) => m.SetScrollSpeed(v)),
                ["Effect"] = Str((m, v) => m.SetEffect(v)),
                ["EffectSpeed"] = Dbl((m, v) => m.SetEffectSpeed(v)),
            };

        public static IReadOnlyCollection<string> Keys => Setters.Keys;

        public static bool IsKnown(string key) => key != null && Setters.ContainsKey(key);

        /// <summary>Apply <paramref name="value"/> via the setter for <paramref name="key"/>; false if unknown or unparsable.</summary>
        public static bool TryApply(AwtrixAppMessage message, string key, string value)
        {
            return key != null && Setters.TryGetValue(key, out var setter) && setter(message, value);
        }

        public static bool IsValidValue(string key, string value) => TryApply(new AwtrixAppMessage(), key, value);

        private static bool Palette(AwtrixAppMessage message, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            if (AwtrixAppMessage.TryParseIntMatrix(value, out var matrix))
            {
                message.SetPalette(matrix);
            }
            else
            {
                message.SetPalette(value.Trim());
            }
            return true;
        }

        private static Func<AwtrixAppMessage, string, bool> Str(Action<AwtrixAppMessage, string> set) =>
            (message, value) =>
            {
                if (value is null)
                {
                    return false;
                }
                set(message, value);
                return true;
            };

        private static Func<AwtrixAppMessage, string, bool> Colour(Action<AwtrixAppMessage, string> set) =>
            (message, value) =>
            {
                try
                {
                    set(message, value);
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            };

        private static Func<AwtrixAppMessage, string, bool> EnumName<T>(Action<AwtrixAppMessage, T> set) where T : struct, System.Enum =>
            (message, value) =>
            {
                // Names only ("pushOnce"), not the underlying numbers ("1"): those are AWTRIX 3 values
                var trimmed = value?.Trim();
                if (string.IsNullOrEmpty(trimmed) || char.IsDigit(trimmed[0]) || trimmed[0] == '-' || !System.Enum.TryParse<T>(trimmed, ignoreCase: true, out var parsed) || !System.Enum.IsDefined(parsed))
                {
                    return false;
                }
                set(message, parsed);
                return true;
            };

        private static Func<AwtrixAppMessage, string, bool> Int(Action<AwtrixAppMessage, int> set) =>
            (message, value) =>
            {
                if (!int.TryParse(value?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }
                set(message, parsed);
                return true;
            };

        private static Func<AwtrixAppMessage, string, bool> Bool(Action<AwtrixAppMessage, bool> set) =>
            (message, value) =>
            {
                if (!bool.TryParse(value?.Trim(), out var parsed))
                {
                    return false;
                }
                set(message, parsed);
                return true;
            };

        private static Func<AwtrixAppMessage, string, bool> Dbl(Action<AwtrixAppMessage, double> set) =>
            (message, value) =>
            {
                // NaN and ±Infinity (including overflow such as 1e999) parse, but are not valid display values
                if (!double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed))
                {
                    return false;
                }
                set(message, parsed);
                return true;
            };

        private static Func<AwtrixAppMessage, string, bool> IntArray(Action<AwtrixAppMessage, int[]> set) =>
            (message, value) =>
            {
                if (!AwtrixAppMessage.TryParseIntArray(value, out var parsed))
                {
                    return false;
                }
                set(message, parsed);
                return true;
            };
    }
}
