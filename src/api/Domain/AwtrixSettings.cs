using System.Text.Json;

namespace AwtrixSharpWeb.Domain
{
    /// <summary>NG settings patch (PATCH /api/v1/settings, {prefix}/cmd/settings). Only the keys set are sent.</summary>
    public class AwtrixSettings : Dictionary<string, object?>
    {
        public AwtrixSettings() : base(StringComparer.Ordinal)
        {
        }

        public AwtrixSettings SetTextColor(string value)
        {
            this["textColor"] = AwtrixColour.Parse(value);
            return this;
        }

        public AwtrixSettings SetBrightness(byte value)
        {
            this["brightness"] = (int)value;
            return this;
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value), AwtrixAppMessage.JsonOptions);
        }

        public override string ToString()
        {
            return string.Join(";", this.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={(kv.Value is string s ? s : JsonSerializer.Serialize(kv.Value, AwtrixAppMessage.JsonOptions))}"));
        }
    }
}
