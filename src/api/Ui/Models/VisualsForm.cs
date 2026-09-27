using System.Globalization;
using System.Text.Json;
using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Ui.Models
{
    public enum VisualsTarget { Notify, CustomApp }

    /// <summary>
    /// The visuals playground form. Build() sets only the fields that differ from "unset" (blank strings,
    /// zero durations, effect speed 1.0, palette blend true) so the JSON stays minimal. Colours and palette
    /// stops that fail to parse are reported by Validate() and skipped by Build(). TryApplyJson() is the
    /// reverse direction: hand-edited JSON is mapped back onto the fields, and keys the form has no field
    /// for are kept in Extras so they still ride along in Build().
    /// </summary>
    public class VisualsForm
    {
        public VisualsTarget Target { get; set; }
        public string AppName { get; set; } = "test";

        public string Text { get; set; } = "Awtrix Sharp!";
        public string TextColor { get; set; } = "#FFFFFF";
        public string BackgroundColor { get; set; } = "";
        public TextCase TextCase { get; set; } = TextCase.Inherit;
        public string Icon { get; set; } = "";
        public IconMode IconMode { get; set; } = IconMode.Fixed;
        public string Font { get; set; } = "";

        public string Effect { get; set; } = "";
        public double EffectSpeed { get; set; } = 1.0;
        public string Palette { get; set; } = "";
        public string PaletteStops { get; set; } = "";
        public bool PaletteBlend { get; set; } = true;
        public string Overlay { get; set; } = "";

        public int DurationMs { get; set; } = 5000;
        public int LifetimeMs { get; set; }
        public bool Hold { get; set; }
        public int TextBlinkMs { get; set; }
        public int TextFadeMs { get; set; }

        public string ScrollMode { get; set; } = "";
        public int ScrollSpeed { get; set; }

        public string TransitionEffect { get; set; } = "";
        public string TransitionDirection { get; set; } = "";
        public int TransitionDurationMs { get; set; }

        /// <summary>Payload keys the form has no field for (or could not interpret), sent verbatim by Build().</summary>
        public Dictionary<string, JsonElement> Extras { get; } = new(StringComparer.Ordinal);

        public void Reset()
        {
            var fresh = new VisualsForm();
            foreach (var property in typeof(VisualsForm).GetProperties().Where(p => p.CanWrite))
            {
                property.SetValue(this, property.GetValue(fresh));
            }
            Extras.Clear();
        }

        /// <summary>
        /// Maps a hand-edited payload onto the fields. Every field except Target and AppName is first
        /// cleared to "unset", then each known key is applied; a known key with an unexpected shape, and
        /// any unknown key, goes to Extras. Invalid JSON returns false and leaves the form untouched.
        /// </summary>
        public bool TryApplyJson(string? json, out string? error)
        {
            if (!AwtrixAppMessage.TryFromJson(json, out var message, out error))
            {
                return false;
            }

            ClearFields();
            foreach (var (key, value) in message)
            {
                var element = value is JsonElement e ? e : JsonSerializer.SerializeToElement(value, AwtrixAppMessage.JsonOptions);
                if (!TryApplyKey(key, element))
                {
                    Extras[key] = element;
                }
            }
            return true;
        }

        private void ClearFields()
        {
            Text = ""; TextColor = ""; BackgroundColor = ""; TextCase = TextCase.Inherit; Icon = ""; IconMode = IconMode.Fixed; Font = "";
            Effect = ""; EffectSpeed = 1.0; Palette = ""; PaletteStops = ""; PaletteBlend = true; Overlay = "";
            DurationMs = 0; LifetimeMs = 0; Hold = false; TextBlinkMs = 0; TextFadeMs = 0;
            ScrollMode = ""; ScrollSpeed = 0;
            TransitionEffect = ""; TransitionDirection = ""; TransitionDurationMs = 0;
            Extras.Clear();
        }

        private bool TryApplyKey(string key, JsonElement e)
        {
            switch (key)
            {
                case "text": return Str(e, v => Text = v);
                case "textColor": return Colour(e, v => TextColor = v);
                case "backgroundColor": return Colour(e, v => BackgroundColor = v);
                case "textCase": return Enum(e, (TextCase v) => TextCase = v);
                case "icon": return Str(e, v => Icon = v);
                case "iconMode": return Enum(e, (IconMode v) => IconMode = v);
                case "font": return Str(e, v => Font = v);
                case "effect": return Str(e, v => Effect = v);
                case "effectSpeed": return Dbl(e, v => EffectSpeed = v);
                case "palette":
                    if (e.ValueKind == JsonValueKind.String) { Palette = e.GetString() ?? ""; return true; }
                    if (e.ValueKind == JsonValueKind.Array && TryRows(e, out var rows)) { PaletteStops = rows; return true; }
                    return false;
                case "paletteBlend": return Bool(e, v => PaletteBlend = v);
                case "overlay": return Str(e, v => Overlay = v);
                case "durationMs": return Int(e, v => DurationMs = v);
                case "lifetimeMs": return Int(e, v => LifetimeMs = v);
                case "hold": return Bool(e, v => Hold = v);
                case "textBlinkMs": return Int(e, v => TextBlinkMs = v);
                case "textFadeMs": return Int(e, v => TextFadeMs = v);
                case "scroll":
                    if (e.ValueKind == JsonValueKind.String) { ScrollMode = e.GetString() ?? ""; return true; }
                    if (e.ValueKind != JsonValueKind.Object) return false;
                    foreach (var p in e.EnumerateObject())
                    {
                        if (p.Name == "mode" && p.Value.ValueKind == JsonValueKind.String) ScrollMode = p.Value.GetString() ?? "";
                        else if (p.Name == "speed" && p.Value.TryGetInt32(out var speed)) ScrollSpeed = speed;
                        else return false; // other scroll keys: keep the whole object verbatim
                    }
                    return true;
                case "transitionEffect": return Str(e, v => TransitionEffect = v);
                case "transitionDirection": return Str(e, v => TransitionDirection = v);
                case "transitionDurationMs": return Int(e, v => TransitionDurationMs = v);
                default: return false;
            }
        }

        private static bool Str(JsonElement e, Action<string> set)
        {
            if (e.ValueKind != JsonValueKind.String) return false;
            set(e.GetString() ?? "");
            return true;
        }

        private static bool Int(JsonElement e, Action<int> set)
        {
            if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt32(out var v)) return false;
            set(v);
            return true;
        }

        private static bool Dbl(JsonElement e, Action<double> set)
        {
            if (e.ValueKind != JsonValueKind.Number || !e.TryGetDouble(out var v)) return false;
            set(v);
            return true;
        }

        private static bool Bool(JsonElement e, Action<bool> set)
        {
            if (e.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            set(e.GetBoolean());
            return true;
        }

        private static bool Enum<T>(JsonElement e, Action<T> set) where T : struct, System.Enum
        {
            if (e.ValueKind != JsonValueKind.String || !System.Enum.TryParse<T>(e.GetString(), ignoreCase: true, out var v)) return false;
            set(v);
            return true;
        }

        /// <summary>"#RRGGBB" stays as typed; [r,g,b] becomes "r,g,b" (the form's other accepted spelling).</summary>
        private static bool Colour(JsonElement e, Action<string> set)
        {
            if (e.ValueKind == JsonValueKind.String) { set(e.GetString() ?? ""); return true; }
            if (e.ValueKind == JsonValueKind.Array && TryRow(e, out var row)) { set(row); return true; }
            return false;
        }

        private static bool TryRow(JsonElement e, out string row)
        {
            row = "";
            var parts = new List<string>();
            foreach (var item in e.EnumerateArray())
            {
                if (!item.TryGetInt32(out var n)) return false;
                parts.Add(n.ToString(CultureInfo.InvariantCulture));
            }
            if (parts.Count != 3) return false;
            row = string.Join(",", parts);
            return true;
        }

        private static bool TryRows(JsonElement e, out string rows)
        {
            rows = "";
            var list = new List<string>();
            foreach (var item in e.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Array || !TryRow(item, out var row)) return false;
                list.Add(row);
            }
            if (list.Count == 0) return false;
            rows = string.Join(";", list);
            return true;
        }

        public AwtrixAppMessage Build()
        {
            var message = new AwtrixAppMessage();
            if (!string.IsNullOrEmpty(Text)) message.SetText(Text);
            if (AwtrixColour.TryParse(TextColor, out _)) message.SetTextColor(TextColor);
            if (AwtrixColour.TryParse(BackgroundColor, out _)) message.SetBackgroundColor(BackgroundColor);
            if (TextCase != TextCase.Inherit) message.SetTextCase(TextCase);
            if (!string.IsNullOrWhiteSpace(Icon)) message.SetIcon(Icon.Trim());
            if (IconMode != IconMode.Fixed) message.SetIconMode(IconMode);
            if (!string.IsNullOrWhiteSpace(Font)) message.SetFont(Font.Trim());

            if (!string.IsNullOrWhiteSpace(Effect)) message.SetEffect(Effect.Trim());
            if (EffectSpeed != 1.0 && InSpeedRange(EffectSpeed)) message.SetEffectSpeed(EffectSpeed);
            if (AwtrixAppMessage.TryParseIntMatrix(PaletteStops, out var stops) && stops.All(row => row.Length == 3))
            {
                message.SetPalette(stops);
            }
            else if (string.IsNullOrWhiteSpace(PaletteStops) && !string.IsNullOrWhiteSpace(Palette))
            {
                message.SetPalette(Palette.Trim());
            }
            if (!PaletteBlend) message.SetPaletteBlend(false);
            if (!string.IsNullOrWhiteSpace(Overlay)) message.SetOverlay(Overlay.Trim());

            if (DurationMs > 0) message.SetDurationMs(DurationMs);
            if (LifetimeMs > 0) message.SetLifetimeMs(LifetimeMs);
            if (Hold) message.SetHold();
            if (TextBlinkMs > 0) message.SetTextBlinkMs(TextBlinkMs);
            if (TextFadeMs > 0) message.SetTextFadeMs(TextFadeMs);

            if (!string.IsNullOrWhiteSpace(ScrollMode) || ScrollSpeed > 0)
            {
                message.SetScroll(string.IsNullOrWhiteSpace(ScrollMode) ? null : ScrollMode.Trim(), ScrollSpeed > 0 ? ScrollSpeed : null);
            }

            if (Target == VisualsTarget.CustomApp)
            {
                if (!string.IsNullOrWhiteSpace(TransitionEffect)) message.SetTransitionEffect(TransitionEffect.Trim());
                if (!string.IsNullOrWhiteSpace(TransitionDirection)) message.SetTransitionDirection(TransitionDirection.Trim());
                if (TransitionDurationMs > 0) message.SetTransitionDurationMs(TransitionDurationMs);
            }

            foreach (var (key, value) in Extras)
            {
                if (!message.ContainsKey(key))
                {
                    message[key] = value;
                }
            }

            return message;
        }

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (!string.IsNullOrWhiteSpace(TextColor) && !AwtrixColour.TryParse(TextColor, out _))
                errors.Add($"Text colour '{TextColor}' is not #RRGGBB, #RGB or r,g,b");
            if (!string.IsNullOrWhiteSpace(BackgroundColor) && !AwtrixColour.TryParse(BackgroundColor, out _))
                errors.Add($"Background colour '{BackgroundColor}' is not #RRGGBB, #RGB or r,g,b");
            if (!string.IsNullOrWhiteSpace(PaletteStops)
                && !(AwtrixAppMessage.TryParseIntMatrix(PaletteStops, out var stops) && stops.All(row => row.Length == 3)))
                errors.Add("Palette stops must be r,g,b rows separated by ';'");
            if (!InSpeedRange(EffectSpeed))
                errors.Add($"Effect speed must be between {NgVisuals.EffectSpeedMin} and {NgVisuals.EffectSpeedMax}");
            if (Target == VisualsTarget.Notify && HasTransition())
                errors.Add("Transition fields only apply to a custom app; they are not sent for a notification");
            if (Target == VisualsTarget.CustomApp && string.IsNullOrWhiteSpace(AppName))
                errors.Add("App name is required for a custom app");
            return errors;
        }

        private bool HasTransition() => !string.IsNullOrWhiteSpace(TransitionEffect) || !string.IsNullOrWhiteSpace(TransitionDirection) || TransitionDurationMs > 0;

        private static bool InSpeedRange(double speed) => speed >= NgVisuals.EffectSpeedMin && speed <= NgVisuals.EffectSpeedMax;
    }
}
