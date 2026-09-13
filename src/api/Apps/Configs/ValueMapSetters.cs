using AwtrixSharpWeb.Domain;
using System.Globalization;

namespace AwtrixSharpWeb.Apps.Configs
{
    /// <summary>
    /// Static map from ValueMap key (case-insensitive, the setter name without "Set") to the
    /// <see cref="AwtrixAppMessage"/> setter it drives. Values are parsed with the invariant culture.
    /// A test asserts every public single-argument Set* method has an entry.
    /// </summary>
    internal static class ValueMapSetters
    {
        private static readonly Dictionary<string, Func<AwtrixAppMessage, string, bool>> Setters =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Text"] = Str((m, v) => m.SetText(v)),
                ["TextCase"] = Int((m, v) => m.SetTextCase(v)),
                ["TopText"] = Bool((m, v) => m.SetTopText(v)),
                ["Hold"] = Bool((m, v) => m.SetHold(v)),
                ["Stack"] = Bool((m, v) => m.SetStack(v)),
                ["TextOffset"] = Int((m, v) => m.SetTextOffset(v)),
                ["Center"] = Bool((m, v) => m.SetCenter(v)),
                ["Color"] = Str((m, v) => m.SetColor(v)),
                ["Gradient"] = IntMatrix((m, v) => m.SetGradient(v)),
                ["BlinkText"] = Dbl((m, v) => m.SetBlinkText(v)),
                ["FadeText"] = Dbl((m, v) => m.SetFadeText(v)),
                ["Background"] = Str((m, v) => m.SetBackground(v)),
                ["Rainbow"] = Bool((m, v) => m.SetRainbow(v)),
                ["Icon"] = Str((m, v) => m.SetIcon(v)),
                ["PushIcon"] = Int((m, v) => m.SetPushIcon(v)),
                ["Duration"] = Int((m, v) => m.SetDuration(v)),
                ["Line"] = IntArray((m, v) => m.SetLine(v)),
                ["Lifetime"] = Int((m, v) => m.SetLifetime(v)),
                ["LifetimeMode"] = Int((m, v) => m.SetLifetimeMode(v)),
                ["Bar"] = IntArray((m, v) => m.SetBar(v)),
                ["Autoscale"] = Bool((m, v) => m.SetAutoscale(v)),
                ["Overlay"] = Str((m, v) => m.SetOverlay(v)),
                ["Progress"] = Int((m, v) => m.SetProgress(v)),
                ["ProgressC"] = IntArray((m, v) => m.SetProgressC(v)),
                ["ProgressBC"] = IntArray((m, v) => m.SetProgressBC(v)),
                ["ScrollSpeed"] = Int((m, v) => m.SetScrollSpeed(v)),
                ["Effect"] = Str((m, v) => m.SetEffect(v)),
                ["EffectSpeed"] = Int((m, v) => m.SetEffectSpeed(v)),
                ["EffectPalette"] = Str((m, v) => m.SetEffectPalette(v)),
                ["EffectBlend"] = Bool((m, v) => m.SetEffectBlend(v)),
            };

        public static IReadOnlyCollection<string> Keys => Setters.Keys;

        public static bool IsKnown(string key) => key != null && Setters.ContainsKey(key);

        /// <summary>Apply <paramref name="value"/> via the setter for <paramref name="key"/>; false if unknown or unparsable.</summary>
        public static bool TryApply(AwtrixAppMessage message, string key, string value)
        {
            return key != null && Setters.TryGetValue(key, out var setter) && setter(message, value);
        }

        public static bool IsValidValue(string key, string value) => TryApply(new AwtrixAppMessage(), key, value);

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

        private static Func<AwtrixAppMessage, string, bool> IntMatrix(Action<AwtrixAppMessage, int[][]> set) =>
            (message, value) =>
            {
                if (!AwtrixAppMessage.TryParseIntMatrix(value, out var parsed))
                {
                    return false;
                }
                set(message, parsed);
                return true;
            };
    }
}
