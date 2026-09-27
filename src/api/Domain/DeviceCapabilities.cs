using System.Text.Json;

namespace AwtrixSharpWeb.Domain
{
    public enum CapabilitiesSource { BuiltIn, Device }

    /// <summary>
    /// The visual vocabularies a device reports on {baseTopic}/state/capabilities. Any list the device
    /// omits (or reports empty) is replaced with the built-in list so the UI never shows an empty dropdown.
    /// </summary>
    public sealed record DeviceCapabilities(
        string[] Effects,
        string[] PaletteEffects,
        string[] Transitions,
        string[] Overlays,
        string[] Palettes,
        CapabilitiesSource Source)
    {
        public static readonly DeviceCapabilities BuiltIn = new(
            NgVisuals.Effects, NgVisuals.PaletteEffects, NgVisuals.Transitions, NgVisuals.Overlays, NgVisuals.Palettes,
            CapabilitiesSource.BuiltIn);

        /// <summary>False (with <see cref="BuiltIn"/>) for anything that is not a JSON object whose lists are string arrays.</summary>
        public static bool TryParse(string? json, out DeviceCapabilities capabilities)
        {
            capabilities = BuiltIn;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (!TryList(doc.RootElement, "effects", NgVisuals.Effects, out var effects)
                    || !TryList(doc.RootElement, "paletteEffects", NgVisuals.PaletteEffects, out var paletteEffects)
                    || !TryList(doc.RootElement, "transitions", NgVisuals.Transitions, out var transitions)
                    || !TryList(doc.RootElement, "overlays", NgVisuals.Overlays, out var overlays)
                    || !TryList(doc.RootElement, "palettes", NgVisuals.Palettes, out var palettes))
                {
                    return false;
                }

                capabilities = new DeviceCapabilities(effects, paletteEffects, transitions, overlays, palettes, CapabilitiesSource.Device);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>Missing or empty → fallback (true). Present but not an array of strings → false.</summary>
        private static bool TryList(JsonElement root, string name, string[] fallback, out string[] list)
        {
            list = fallback;
            if (!root.TryGetProperty(name, out var element))
            {
                return true;
            }
            if (element.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var items = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    return false;
                }
                items.Add(item.GetString()!);
            }

            if (items.Count > 0)
            {
                list = items.ToArray();
            }
            return true;
        }
    }
}
