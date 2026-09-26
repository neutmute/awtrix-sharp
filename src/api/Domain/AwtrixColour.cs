using System.Text.RegularExpressions;

namespace AwtrixSharpWeb.Domain
{
    /// <summary>
    /// NG colour forms (https://blueforcer.github.io/awtrix-ng/reference/payload/): "#RRGGBB", "#RGB" or [r,g,b].
    /// Config values are strings, so "r,g,b" is accepted and becomes an int[3]. Anything else (including a bare
    /// "RRGGBB" from an AWTRIX 3 config) is rejected; see docs/config-migration.md.
    /// </summary>
    public static class AwtrixColour
    {
        private static readonly Regex Hex = new("^#([0-9a-fA-F]{6}|[0-9a-fA-F]{3})$", RegexOptions.Compiled);

        /// <returns>The trimmed "#…" string, or an int[3].</returns>
        /// <exception cref="ArgumentException">The value is not an NG colour.</exception>
        public static object Parse(string value)
        {
            if (TryParse(value, out var colour))
            {
                return colour!;
            }
            throw new ArgumentException($"'{value}' is not a colour; expected #RRGGBB, #RGB or r,g,b", nameof(value));
        }

        public static bool TryParse(string? value, out object? colour)
        {
            colour = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();
            if (Hex.IsMatch(trimmed))
            {
                colour = trimmed;
                return true;
            }

            if (AwtrixAppMessage.TryParseIntArray(trimmed, out var rgb) && rgb.Length == 3 && rgb.All(c => c is >= 0 and <= 255))
            {
                colour = rgb;
                return true;
            }

            return false;
        }
    }
}
