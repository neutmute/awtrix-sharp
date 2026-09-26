using System.Text.Json.Serialization;

namespace AwtrixSharpWeb.Domain
{
    /// <summary>One coloured run of an NG "text" array: [{"text":"…","color":"#RRGGBB"}]. Color is optional.</summary>
    public sealed class TextFragment
    {
        public TextFragment(string text, string? color = null)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Color = color == null ? null : AwtrixColour.Parse(color);
        }

        [JsonPropertyName("text")]
        public string Text { get; }

        /// <summary>"#…" string or int[3]; omitted from JSON when null.</summary>
        [JsonPropertyName("color")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Color { get; }
    }
}
