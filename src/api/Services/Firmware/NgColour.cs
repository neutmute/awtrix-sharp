using System.Text.RegularExpressions;
using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Services.Firmware
{
    /// <summary>
    /// NG accepts "#RRGGBB", "#RGB" and [r,g,b]. AWTRIX 3 configs also carry bare "RRGGBB" (TripTimerApp fragments)
    /// and "r,g,b" strings (progressC), so normalise those. Anything else is null: the caller drops the key.
    /// </summary>
    internal static class NgColour
    {
        private static readonly Regex Hex = new("^#?([0-9a-fA-F]{6}|[0-9a-fA-F]{3})$", RegexOptions.Compiled);

        public static object? Normalise(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            var match = Hex.Match(trimmed);
            if (match.Success)
            {
                return "#" + match.Groups[1].Value;
            }

            if (AwtrixAppMessage.TryParseIntArray(trimmed, out var rgb) && rgb.Length == 3)
            {
                return rgb;
            }

            return null;
        }
    }
}
