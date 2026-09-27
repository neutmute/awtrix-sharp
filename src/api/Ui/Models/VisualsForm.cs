using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Ui.Models
{
    public enum VisualsTarget { Notify, CustomApp }

    /// <summary>
    /// The visuals playground form. Build() sets only the fields that differ from "unset" (blank strings,
    /// zero durations, effect speed 1.0, palette blend true) so the JSON stays minimal. Colours and palette
    /// stops that fail to parse are reported by Validate() and skipped by Build().
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

        public void Reset()
        {
            var fresh = new VisualsForm();
            foreach (var property in typeof(VisualsForm).GetProperties())
            {
                property.SetValue(this, property.GetValue(fresh));
            }
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
