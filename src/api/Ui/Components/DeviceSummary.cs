using System.Text.Json;

namespace AwtrixSharpWeb.Ui.Components
{
    public static class DeviceSummary
    {
        private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

        /// <summary>"version X, ip Y" from the device payload when present; else "state received HH:mm:ss"; else "no state received yet".</summary>
        public static string Describe(string? deviceJson, DateTimeOffset? lastSeen)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(deviceJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(deviceJson);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var key in new[] { "version", "firmware", "ip" })
                        {
                            if (doc.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                            {
                                parts.Add($"{key} {value.GetString()}");
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                }
            }

            if (parts.Count > 0)
            {
                return string.Join(", ", parts);
            }
            return lastSeen.HasValue ? $"state received {lastSeen.Value.ToLocalTime():HH:mm:ss}" : "no state received yet";
        }

        public static string Pretty(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return string.Empty;
            }
            try
            {
                using var doc = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(doc.RootElement, Indented);
            }
            catch (JsonException)
            {
                return json;
            }
        }
    }
}
