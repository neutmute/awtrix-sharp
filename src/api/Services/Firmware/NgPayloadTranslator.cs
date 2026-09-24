using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Services.Firmware
{
    public enum NgPayloadKind { App, Notification }

    /// <summary>Json is the NG body; DroppedKeys are message keys with no NG equivalent or an unparsable value.</summary>
    public sealed record NgTranslation(string Json, IReadOnlyList<string> DroppedKeys);

    /// <summary>
    /// Converts an AWTRIX 3 vocabulary <see cref="AwtrixAppMessage"/> into an NG pushed-app or notification body
    /// (https://blueforcer.github.io/awtrix-ng/reference/payload/). Values are emitted typed because NG validates
    /// types and rejects the whole payload on any unknown key. Keys are emitted text-first then sorted so tests
    /// can compare strings. Rules (see spec): palette precedence gradient > rainbow > effectPalette; a palette
    /// forces textColor "palette" only when it came from rainbow or gradient (not a bare effectPalette); hold/stack
    /// are notification-only; lifetime* are app-only.
    /// </summary>
    public static class NgPayloadTranslator
    {
        private static readonly HashSet<string> NotificationOnly = new(StringComparer.Ordinal) { "hold", "stack" };
        private static readonly HashSet<string> AppOnly = new(StringComparer.Ordinal) { "lifetime", "lifetimeMode" };
        private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        public static NgTranslation Translate(AwtrixAppMessage message, NgPayloadKind kind)
        {
            var output = new Dictionary<string, object?>(StringComparer.Ordinal);
            var dropped = new List<string>();
            object? palette = null;
            var palettePriority = -1; // 0 effectPalette, 1 rainbow, 2 gradient
            int? scrollSpeed = null;

            foreach (var (key, raw) in message)
            {
                if (kind == NgPayloadKind.App && NotificationOnly.Contains(key)) continue;
                if (kind == NgPayloadKind.Notification && AppOnly.Contains(key)) continue;

                bool ok;
                switch (key)
                {
                    case "text":
                        ok = Put(output, "text", TranslateText(raw));
                        break;
                    case "textCase":
                        ok = PutEnum(output, "textCase", raw, "inherit", "upper", "asTyped");
                        break;
                    case "hold":
                        ok = PutBool(output, "hold", raw);
                        break;
                    case "stack":
                        ok = PutBool(output, "stack", raw);
                        break;
                    case "textOffset":
                        ok = PutInt(output, "textOffsetX", raw);
                        break;
                    case "center":
                        ok = PutBool(output, "textCenter", raw);
                        break;
                    case "color":
                        ok = Put(output, "textColor", NgColour.Normalise(raw));
                        break;
                    case "background":
                        ok = Put(output, "backgroundColor", NgColour.Normalise(raw));
                        break;
                    case "gradient":
                        ok = TryMatrix(raw, out var matrix);
                        if (ok) { palettePriority = SetPriority(palettePriority, 2, ref palette, matrix); }
                        break;
                    case "rainbow":
                        ok = TryBool(raw, out var rainbow);
                        if (ok && rainbow) { palettePriority = SetPriority(palettePriority, 1, ref palette, "Rainbow"); }
                        break;
                    case "effectPalette":
                        ok = !string.IsNullOrWhiteSpace(raw);
                        if (ok) { palettePriority = SetPriority(palettePriority, 0, ref palette, raw!); }
                        break;
                    case "blinkText":
                        ok = PutInt(output, "textBlinkMs", raw, allowDouble: true);
                        break;
                    case "fadeText":
                        ok = PutInt(output, "textFadeMs", raw, allowDouble: true);
                        break;
                    case "icon":
                        ok = Put(output, "icon", raw);
                        break;
                    case "pushIcon":
                        ok = PutEnum(output, "iconMode", raw, "fixed", "pushOnce", "push");
                        break;
                    case "duration":
                        ok = PutSecondsAsMs(output, "durationMs", raw);
                        break;
                    case "lifetime":
                        ok = PutSecondsAsMs(output, "lifetimeMs", raw);
                        break;
                    case "lifetimeMode":
                        ok = PutEnum(output, "lifetimeExpiry", raw, "remove", "mark");
                        break;
                    case "line":
                        ok = PutIntArray(output, "lineChart", raw);
                        break;
                    case "bar":
                        ok = PutIntArray(output, "barChart", raw);
                        break;
                    case "autoscale":
                        ok = PutBool(output, "chartAutoscale", raw);
                        break;
                    case "overlay":
                        ok = Put(output, "overlay", raw);
                        break;
                    case "progress":
                        ok = PutInt(output, "progress", raw);
                        break;
                    case "progressC":
                        ok = Put(output, "progressColor", NgColour.Normalise(raw));
                        break;
                    case "progressBC":
                        ok = Put(output, "progressTrackColor", NgColour.Normalise(raw));
                        break;
                    case "scrollSpeed":
                        ok = TryInt(raw, out var speed);
                        if (ok) scrollSpeed = speed;
                        break;
                    case "effect":
                        ok = Put(output, "effect", raw);
                        break;
                    case "effectSpeed":
                        ok = PutNumber(output, "effectSpeed", raw);
                        break;
                    case "effectBlend":
                        ok = PutBool(output, "paletteBlend", raw);
                        break;
                    default:
                        ok = false; // topText and anything unknown
                        break;
                }

                if (!ok)
                {
                    dropped.Add(key);
                }
            }

            if (palette != null)
            {
                output["palette"] = palette;
                if (palettePriority >= 1)
                {
                    output["textColor"] = "palette";
                }
            }

            if (scrollSpeed.HasValue)
            {
                output["scroll"] = new Dictionary<string, object> { ["speed"] = scrollSpeed.Value };
            }

            var ordered = output
                .OrderBy(kvp => kvp.Key == "text" ? "" : kvp.Key, StringComparer.Ordinal)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            return new NgTranslation(JsonSerializer.Serialize(ordered, JsonOptions), dropped);
        }

        private static int SetPriority(int currentPriority, int candidatePriority, ref object? palette, object candidateValue)
        {
            if (candidatePriority >= currentPriority)
            {
                palette = candidateValue;
                return candidatePriority;
            }
            return currentPriority;
        }

        private static bool Put(Dictionary<string, object?> output, string key, object? value)
        {
            if (value == null) return false;
            output[key] = value;
            return true;
        }

        private static bool TryInt(string? raw, out int value) => int.TryParse(raw?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        private static bool TryBool(string? raw, out bool value) => bool.TryParse(raw?.Trim(), out value);
        private static bool TryMatrix(string? raw, out int[][] value) => AwtrixAppMessage.TryParseIntMatrix(raw, out value);

        private static bool PutInt(Dictionary<string, object?> output, string key, string? raw, bool allowDouble = false)
        {
            if (TryInt(raw, out var i)) return Put(output, key, i);
            if (allowDouble && double.TryParse(raw?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d))
                return Put(output, key, (int)Math.Round(d));
            return false;
        }

        private static bool PutNumber(Dictionary<string, object?> output, string key, string? raw)
        {
            if (TryInt(raw, out var i)) return Put(output, key, i);
            return double.TryParse(raw?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) && Put(output, key, d);
        }

        private static bool PutBool(Dictionary<string, object?> output, string key, string? raw) => TryBool(raw, out var b) && Put(output, key, b);

        private static bool PutIntArray(Dictionary<string, object?> output, string key, string? raw) => AwtrixAppMessage.TryParseIntArray(raw, out var a) && Put(output, key, a);

        private static bool PutSecondsAsMs(Dictionary<string, object?> output, string key, string? raw) => TryInt(raw, out var s) && Put(output, key, (long)s * 1000);

        private static bool PutEnum(Dictionary<string, object?> output, string key, string? raw, params string[] names)
            => TryInt(raw, out var i) && i >= 0 && i < names.Length && Put(output, key, names[i]);

        /// <summary>AWTRIX 3 fragments are [{"t","c"}]; NG wants [{"text","color"}]. Anything unparsable is sent as the plain string.</summary>
        private static object? TranslateText(string? raw)
        {
            if (raw == null) return null;
            if (!raw.StartsWith("[")) return raw;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return raw;
                var fragments = new List<Dictionary<string, object?>>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var fragment = new Dictionary<string, object?>();
                    if (el.TryGetProperty("t", out var t)) fragment["text"] = t.GetString();
                    else if (el.TryGetProperty("text", out var t2)) fragment["text"] = t2.GetString();

                    if (el.TryGetProperty("c", out var c)) fragment["color"] = NgColour.Normalise(c.GetString());
                    else if (el.TryGetProperty("color", out var c2)) fragment["color"] = NgColour.Normalise(c2.GetString());

                    if (fragment.TryGetValue("color", out var colour) && colour is null) fragment.Remove("color");
                    fragments.Add(fragment);
                }
                return fragments;
            }
            catch (JsonException)
            {
                return raw;
            }
        }
    }
}
