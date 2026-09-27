namespace AwtrixSharpWeb.Domain
{
    /// <summary>
    /// Built-in AWTRIX NG visual vocabularies (https://blueforcer.github.io/awtrix-ng/reference/visuals/),
    /// used when a device has not published its capabilities.
    /// </summary>
    public static class NgVisuals
    {
        public static readonly string[] Effects =
        {
            "Plasma", "TheaterChase", "Fade", "MovingLine", "BrickBreaker", "PingPong", "Radar", "Checkerboard",
            "Fireworks", "PlasmaCloud", "Ripple", "Snake", "Pacifica", "Matrix", "SwirlIn", "SwirlOut",
            "LookingEyes", "TwinklingStars", "ColorWaves",
        };

        public static readonly string[] PaletteEffects =
        {
            "Checkerboard", "ColorWaves", "Fade", "Fireworks", "MovingLine", "Pacifica", "Plasma", "PlasmaCloud",
            "Radar", "Ripple", "Snake", "SwirlIn", "SwirlOut", "TheaterChase", "TwinklingStars",
        };

        public static readonly string[] Transitions =
        {
            "Random", "Slide", "Dim", "Zoom", "Rotate", "Pixelate", "Curtain", "Ripple", "Blink", "Reload", "Fade",
            "Cover", "Uncover", "Split", "Blinds", "Blocks", "Flash", "Diamond", "Wave", "Rain", "Melt", "Interlace",
        };

        public static readonly string[] Overlays = { "rain", "snow", "drizzle", "storm", "thunder", "frost" };

        public static readonly string[] Palettes = { "Cloud", "Lava", "Ocean", "Forest", "Stripe", "Party", "Heat", "Rainbow" };

        public static readonly string[] TransitionDirections = { "normal", "reverse" };

        public static readonly string[] Fonts = { "small", "large" };

        public static readonly string[] ScrollModes = { "static", "wrap", "loop", "bounce" };

        public const double EffectSpeedMin = 0.1;
        public const double EffectSpeedMax = 10;
    }
}
