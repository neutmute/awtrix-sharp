using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AwtrixSharpWeb.Domain
{
    /// <summary>
    /// An AWTRIX NG pushed-app or notification payload (https://blueforcer.github.io/awtrix-ng/reference/payload/).
    /// Keys are NG names; values are stored typed by the setters so <see cref="ToJson()"/> is a plain serialize.
    /// Only keys that were set are sent: NG rejects unknown keys and treats absent keys as defaults.
    /// </summary>
    public class AwtrixAppMessage : Dictionary<string, object?>
    {
        private const string TextKey = "text";

        internal static readonly JsonSerializerOptions JsonOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        public AwtrixAppMessage() : base(StringComparer.Ordinal)
        {
        }

        /// <summary>The plain text, or null when unset or when the text is a fragment array.</summary>
        public string? Text => TryGetValue(TextKey, out var value) switch
        {
            true when value is string s => s,
            true when value is JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
            _ => null,
        };

        public AwtrixAppMessage SetText(string? value) => Put(TextKey, value);

        public AwtrixAppMessage SetText(IEnumerable<TextFragment> fragments) => Put(TextKey, fragments.ToArray());

        public AwtrixAppMessage SetTextCase(TextCase value) => Put("textCase", value);

        public AwtrixAppMessage SetHold(bool value = true) => Put("hold", value);

        public AwtrixAppMessage SetStack(bool value = true) => Put("stack", value);

        public AwtrixAppMessage SetTextOffsetX(int value) => Put("textOffsetX", value);

        public AwtrixAppMessage SetTextCenter(bool value) => Put("textCenter", value);

        /// <param name="value">A colour, or the literal "palette" to colour text from the palette.</param>
        public AwtrixAppMessage SetTextColor(string value)
            => Put("textColor", string.Equals(value, "palette", StringComparison.OrdinalIgnoreCase) ? "palette" : AwtrixColour.Parse(value));

        public AwtrixAppMessage SetBackgroundColor(string value) => Put("backgroundColor", AwtrixColour.Parse(value));

        public AwtrixAppMessage SetPalette(string? name) => Put("palette", name);

        public AwtrixAppMessage SetPalette(int[][] colours)
        {
            if (colours != null && colours.Length > 0)
            {
                Put("palette", colours);
            }
            return this;
        }

        public AwtrixAppMessage SetPaletteBlend(bool value) => Put("paletteBlend", value);

        public AwtrixAppMessage SetTextBlinkMs(int value) => Put("textBlinkMs", value);

        public AwtrixAppMessage SetTextFadeMs(int value) => Put("textFadeMs", value);

        public AwtrixAppMessage SetIcon(string? value) => Put("icon", value);

        public AwtrixAppMessage SetIconMode(IconMode value) => Put("iconMode", value);

        public AwtrixAppMessage SetDurationMs(int value) => Put("durationMs", value);

        public AwtrixAppMessage SetDuration(TimeSpan value) => SetDurationMs(Convert.ToInt32(value.TotalMilliseconds));

        public AwtrixAppMessage SetLifetimeMs(int value) => Put("lifetimeMs", value);

        public AwtrixAppMessage SetLifetime(TimeSpan value) => SetLifetimeMs(Convert.ToInt32(value.TotalMilliseconds));

        public AwtrixAppMessage SetLifetimeExpiry(LifetimeExpiry value) => Put("lifetimeExpiry", value);

        public AwtrixAppMessage SetLineChart(int[] value) => Put("lineChart", value);

        public AwtrixAppMessage SetBarChart(int[] value) => Put("barChart", value);

        public AwtrixAppMessage SetChartAutoscale(bool value) => Put("chartAutoscale", value);

        public AwtrixAppMessage SetOverlay(string? value) => Put("overlay", value);

        public AwtrixAppMessage SetProgress(int value) => Put("progress", value);

        public AwtrixAppMessage SetProgressColor(string value) => Put("progressColor", AwtrixColour.Parse(value));

        public AwtrixAppMessage SetProgressTrackColor(string value) => Put("progressTrackColor", AwtrixColour.Parse(value));

        /// <summary>Sets NG "scroll.speed", keeping any "scroll.mode" already present.</summary>
        public AwtrixAppMessage SetScrollSpeed(int value) => MergeScroll("speed", value);

        /// <summary>Sets NG "scroll.mode" (static, wrap, loop, bounce), keeping any "scroll.speed" already present.</summary>
        public AwtrixAppMessage SetScrollMode(string? value) => MergeScroll("mode", string.IsNullOrWhiteSpace(value) ? null : value);

        private AwtrixAppMessage MergeScroll(string part, object? value)
        {
            var scroll = TryGetValue("scroll", out var existing) && existing is Dictionary<string, object> dict
                ? new Dictionary<string, object>(dict)
                : new Dictionary<string, object>();
            if (value is null)
            {
                scroll.Remove(part);
            }
            else
            {
                scroll[part] = value;
            }
            return Put("scroll", scroll.Count == 0 ? null : scroll);
        }

        public AwtrixAppMessage SetEffect(string? value) => Put("effect", value);

        public AwtrixAppMessage SetEffectSpeed(double value) => Put("effectSpeed", value);

        public AwtrixAppMessage SetFont(string? value) => Put("font", value);

        /// <summary>NG "scroll" object. Null parts are omitted; both null removes the key.</summary>
        public AwtrixAppMessage SetScroll(string? mode, int? speed)
        {
            var scroll = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(mode))
            {
                scroll["mode"] = mode;
            }
            if (speed.HasValue)
            {
                scroll["speed"] = speed.Value;
            }
            return Put("scroll", scroll.Count == 0 ? null : scroll);
        }

        public AwtrixAppMessage SetTransitionEffect(string? value) => Put("transitionEffect", value);

        public AwtrixAppMessage SetTransitionDirection(string? value) => Put("transitionDirection", value);

        public AwtrixAppMessage SetTransitionDurationMs(int value) => Put("transitionDurationMs", value);

        /// <summary>
        /// Parses a JSON object into a message whose values are JsonElements (the serializer writes them back
        /// verbatim), so hand-edited JSON follows the same send path as built messages. Non-objects and parse
        /// failures return false with a human-readable error.
        /// </summary>
        public static bool TryFromJson(string? json, out AwtrixAppMessage message, out string? error)
        {
            message = new AwtrixAppMessage();
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "JSON is empty";
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    error = "JSON must be an object";
                    return false;
                }
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    message[property.Name] = property.Value.Clone();
                }
                return true;
            }
            catch (JsonException ex)
            {
                error = $"Invalid JSON: {ex.Message}";
                return false;
            }
        }

        /// <summary>A null value removes the key rather than storing it: System.Text.Json's WhenWritingNull
        /// does not suppress null dictionary values, so a stored null would still serialize as e.g. "text":null.</summary>
        private AwtrixAppMessage Put(string key, object? value)
        {
            if (value is null)
            {
                Remove(key);
            }
            else
            {
                this[key] = value;
            }
            return this;
        }

        /// <summary>Parse "1,2,3" (whitespace around items allowed) into integers using the invariant culture.</summary>
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

        /// <summary>Parse "255,0,0;0,255,0" into rows of integers using the invariant culture.</summary>
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
            return string.Join("; ", Ordered(Array.Empty<string>()).Select(kvp => $"{kvp.Key}={(kvp.Value is string s ? s : JsonSerializer.Serialize(kvp.Value, JsonOptions))}"));
        }

        public string ToJson() => ToJson(Array.Empty<string>());

        /// <summary>Serializes every key except <paramref name="excludedKeys"/>; text first, then keys sorted ordinally.</summary>
        public string ToJson(params string[] excludedKeys)
        {
            return JsonSerializer.Serialize(Ordered(excludedKeys).ToDictionary(kvp => kvp.Key, kvp => kvp.Value), JsonOptions);
        }

        private IEnumerable<KeyValuePair<string, object?>> Ordered(string[] excludedKeys)
        {
            return this
                .Where(kvp => !excludedKeys.Contains(kvp.Key, StringComparer.Ordinal))
                .OrderBy(kvp => kvp.Key == TextKey ? "" : kvp.Key, StringComparer.Ordinal);
        }
    }
}
