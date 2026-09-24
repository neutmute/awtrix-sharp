using System.Globalization;
using System.Text.Json;
using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Services.Firmware
{
    /// <summary>AWTRIX 3 settings keys → NG PATCH /api/v1/settings keys. BRI→brightness, TCOL→textColor; others dropped.</summary>
    public static class NgSettingsTranslator
    {
        public static NgTranslation Translate(AwtrixSettings settings)
        {
            var output = new SortedDictionary<string, object>(StringComparer.Ordinal);
            var dropped = new List<string>();

            foreach (var (key, raw) in settings)
            {
                var ok = key switch
                {
                    "BRI" => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bri) && Add(output, "brightness", bri),
                    "TCOL" => NgColour.Normalise(raw) is { } colour && Add(output, "textColor", colour),
                    _ => false,
                };
                if (!ok)
                {
                    dropped.Add(key);
                }
            }

            return new NgTranslation(JsonSerializer.Serialize(output), dropped);
        }

        private static bool Add(SortedDictionary<string, object> output, string key, object value)
        {
            output[key] = value;
            return true;
        }
    }
}
