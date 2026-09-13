using System.Globalization;
using System.Text.Json;

namespace AwtrixSharpWeb.Domain
{

    /// <summary>
    /// Dictionary-based implementation of an Awtrix application message
    /// that stores all properties as string key-value pairs without default values.
    /// Numbers are always formatted with the invariant culture. Array-valued keys are stored as
    /// comma-separated strings ("1,2,3"; gradient "255,0,0;0,255,0") and emitted as JSON arrays by <see cref="ToJson"/>.
    /// </summary>
    public class AwtrixAppMessage : Dictionary<string, string>
    {
        private const string TextKey = "text";
        private const string GradientKey = "gradient";

        private static readonly HashSet<string> IntArrayKeys = new(StringComparer.Ordinal)
        {
            "line", "bar", "progressC", "progressBC"
        };

        private string Get(string key)
        {
            if (this.TryGetValue(key, out var value))
            {
                return value;
            }
            return null;
        }

        public string Text => Get(TextKey);



        public AwtrixAppMessage SetText(string value)
        {
            this[TextKey] = value;
            return this;
        }

        public AwtrixAppMessage SetTextCase(int value) => SetInt("textCase", value);

        public AwtrixAppMessage SetTopText(bool value)
        {
            return Set("topText", value);
        }


        public AwtrixAppMessage SetHold(bool value = true)
        {
            return Set("hold", value);
        }

        public AwtrixAppMessage SetStack(bool value = true)
        {
            return Set("stack", value);
        }


        public AwtrixAppMessage SetTextOffset(int value) => SetInt("textOffset", value);

        public AwtrixAppMessage SetCenter(bool value)
        {
            return Set("center", value);
        }
        public AwtrixAppMessage SetColor(string value)
        {
            this["color"] = value;
            return this;
        }

        public AwtrixAppMessage SetGradient(int[][] value)
        {
            if (value != null && value.Length > 0)
            {
                this[GradientKey] = string.Join(';', value.Select(JoinInts));
            }
            return this;
        }

        public AwtrixAppMessage SetBlinkText(double value)
        {
            this["blinkText"] = value.ToString(CultureInfo.InvariantCulture);
            return this;
        }

        public AwtrixAppMessage SetFadeText(double value)
        {
            this["fadeText"] = value.ToString(CultureInfo.InvariantCulture);
            return this;
        }

        public AwtrixAppMessage SetBackground(string value)
        {
            this["background"] = value;
            return this;
        }


        public AwtrixAppMessage SetRainbow(bool value = true)
        {
            return Set("rainbow", value);
        }

        public AwtrixAppMessage SetIcon(string value)
        {
            this["icon"] = value;
            return this;
        }

        public AwtrixAppMessage SetPushIcon(int value) => SetInt("pushIcon", value);

        public AwtrixAppMessage SetDuration(int value)
        {
            SetDuration(TimeSpan.FromSeconds(value));
            return this;
        }

        public AwtrixAppMessage SetDuration(TimeSpan value) => SetInt("duration", Convert.ToInt32(value.TotalSeconds));

        public AwtrixAppMessage SetLine(int[] value)
        {
            this["line"] = JoinInts(value);
            return this;
        }

        public AwtrixAppMessage SetLifetime(int value) => SetInt("lifetime", value);

        public AwtrixAppMessage SetLifetimeMode(int value) => SetInt("lifetimeMode", value);

        public AwtrixAppMessage SetBar(int[] value)
        {
            this["bar"] = JoinInts(value);
            return this;
        }

        public AwtrixAppMessage SetAutoscale(bool value) => Set("autoscale", value);

        public AwtrixAppMessage SetOverlay(string value)
        {
            this["overlay"] = value;
            return this;
        }

        public AwtrixAppMessage SetProgress(int value) => SetInt("progress", value);

        public AwtrixAppMessage SetProgressC(int[] value)
        {
            this["progressC"] = JoinInts(value);
            return this;
        }

        public AwtrixAppMessage SetProgressBC(int[] value)
        {
            this["progressBC"] = JoinInts(value);
            return this;
        }

        public AwtrixAppMessage SetScrollSpeed(int value) => SetInt("scrollSpeed", value);

        public AwtrixAppMessage SetEffect(string value)
        {
            this["effect"] = value;
            return this;
        }

        public AwtrixAppMessage SetEffectSpeed(int value) => SetInt("effectSpeed", value);

        public AwtrixAppMessage SetEffectPalette(string value)
        {
            this["effectPalette"] = value;
            return this;
        }

        public AwtrixAppMessage SetEffectBlend(bool value) => Set("effectBlend", value);


        private AwtrixAppMessage Set(string key, bool value)
        {
            this[key] = value.ToString().ToLower();
            return this;
        }

        private AwtrixAppMessage SetInt(string key, int value)
        {
            this[key] = value.ToString(CultureInfo.InvariantCulture);
            return this;
        }

        private static string JoinInts(int[] values)
        {
            return string.Join(',', values.Select(v => v.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Parse "1,2,3" (whitespace around items allowed) into integers using the invariant culture.
        /// </summary>
        internal static bool TryParseIntArray(string? value, out int[] result)
        {
            result = Array.Empty<int>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var parts = value.Split(',', StringSplitOptions.TrimEntries);
            var parsed = new int[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out parsed[i]))
                {
                    return false;
                }
            }

            result = parsed;
            return true;
        }

        /// <summary>
        /// Parse "255,0,0;0,255,0" into rows of integers using the invariant culture.
        /// </summary>
        internal static bool TryParseIntMatrix(string? value, out int[][] result)
        {
            result = Array.Empty<int[]>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var rows = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (rows.Length == 0)
            {
                return false;
            }

            var parsed = new int[rows.Length][];
            for (var i = 0; i < rows.Length; i++)
            {
                if (!TryParseIntArray(rows[i], out parsed[i]))
                {
                    return false;
                }
            }

            result = parsed;
            return true;
        }

        public override string ToString()
        {
            return string.Join(
                "; ",
                this.OrderBy(kvp => kvp.Key == "Text" ? "" : kvp.Key)       // always name first
                    .Select(kvp => $"{kvp.Key}={kvp.Value}")
            );
        }

        public string ToJson()
        {
            var dictionaryToSerialize = new Dictionary<string, object>(this.Count);

            foreach (var kvp in this)
            {
                dictionaryToSerialize[kvp.Key] = ToJsonValue(kvp.Key, kvp.Value);
            }

            return JsonSerializer.Serialize(dictionaryToSerialize);
        }

        private static object ToJsonValue(string key, string value)
        {
            // Text starting with "[" is an encoded JSON array of coloured text fragments
            if (key == TextKey && value != null && value.StartsWith("["))
            {
                try
                {
                    return JsonSerializer.Deserialize<JsonElement>(value);
                }
                catch
                {
                    return value;
                }
            }

            if (IntArrayKeys.Contains(key) && TryParseIntArray(value, out var array))
            {
                return array;
            }

            if (key == GradientKey && TryParseIntMatrix(value, out var matrix))
            {
                return matrix;
            }

            return value;
        }
    }
}
