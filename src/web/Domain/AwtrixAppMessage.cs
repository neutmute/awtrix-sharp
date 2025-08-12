using System.Text.Json.Serialization;

namespace AwtrixSharpWeb.Domain
{
    /// <summary>
    /// Represents an Awtrix application with its properties and default settings
    /// </summary>
    public class AwtrixAppMessage
    {
        // Default values as constants
        private static readonly string DefaultText = "New Awtrix App";
        private static readonly int DefaultTextCase = 0;
        private static readonly bool DefaultTopText = false;
        private static readonly int DefaultTextOffset = 0;
        private static readonly bool DefaultCenter = true;
        private static readonly int[] DefaultColor = { 255, 255, 255 };
        private static readonly int[][] DefaultGradient = Array.Empty<int[]>();
        private static readonly double DefaultBlinkText = 0;
        private static readonly double DefaultFadeText = 0;
        private static readonly int[] DefaultBackground = { 0, 0, 0 };
        private static readonly bool DefaultRainbow = false;
        private static readonly string DefaultIcon = "None";
        private static readonly int DefaultPushIcon = 0;
        private static readonly int DefaultDuration = 7;
        private static readonly int[] DefaultLine = Array.Empty<int>();
        private static readonly int DefaultLifetime = 0;
        private static readonly int DefaultLifetimeMode = 0;
        private static readonly int[] DefaultBar = Array.Empty<int>();
        private static readonly bool DefaultAutoscale = true;
        private static readonly string DefaultOverlay = "Clear";
        private static readonly int DefaultProgress = -1;
        private static readonly int[] DefaultProgressC = { 0, 255, 0 };
        private static readonly int[] DefaultProgressBC = { 255, 255, 255 };
        private static readonly int DefaultScrollSpeed = 100;
        private static readonly string DefaultEffect = "None";
        private static readonly int DefaultEffectSpeed = 100;
        private static readonly string DefaultEffectPalette = "None";
        private static readonly bool DefaultEffectBlend = true;

        // Properties with default values
        [JsonPropertyName("text")]
        public string Text { get; set; } = DefaultText;

        [JsonPropertyName("textCase")]
        public int TextCase { get; set; } = DefaultTextCase;

        [JsonPropertyName("topText")]
        public bool TopText { get; set; } = DefaultTopText;

        [JsonPropertyName("textOffset")]
        public int TextOffset { get; set; } = DefaultTextOffset;

        [JsonPropertyName("center")]
        public bool Center { get; set; } = DefaultCenter;

        [JsonPropertyName("color")]
        public int[] Color { get; set; } = DefaultColor.Clone() as int[];

        [JsonPropertyName("gradient")]
        public int[][] Gradient { get; set; } = DefaultGradient.Clone() as int[][];

        [JsonPropertyName("blinkText")]
        public double BlinkText { get; set; } = DefaultBlinkText;

        [JsonPropertyName("fadeText")]
        public double FadeText { get; set; } = DefaultFadeText;

        [JsonPropertyName("background")]
        public int[] Background { get; set; } = DefaultBackground.Clone() as int[];

        [JsonPropertyName("rainbow")]
        public bool Rainbow { get; set; } = DefaultRainbow;

        [JsonPropertyName("icon")]
        public string Icon { get; set; } = DefaultIcon;

        [JsonPropertyName("pushIcon")]
        public int PushIcon { get; set; } = DefaultPushIcon;

        [JsonPropertyName("duration")]
        public int Duration { get; set; } = DefaultDuration;

        [JsonPropertyName("line")]
        public int[] Line { get; set; } = DefaultLine.Clone() as int[];

        [JsonPropertyName("lifetime")]
        public int Lifetime { get; set; } = DefaultLifetime;

        [JsonPropertyName("lifetimeMode")]
        public int LifetimeMode { get; set; } = DefaultLifetimeMode;

        [JsonPropertyName("bar")]
        public int[] Bar { get; set; } = DefaultBar.Clone() as int[];

        [JsonPropertyName("autoscale")]
        public bool Autoscale { get; set; } = DefaultAutoscale;

        [JsonPropertyName("overlay")]
        public string Overlay { get; set; } = DefaultOverlay;

        [JsonPropertyName("progress")]
        public int Progress { get; set; } = DefaultProgress;

        [JsonPropertyName("progressC")]
        public int[] ProgressC { get; set; } = DefaultProgressC.Clone() as int[];

        [JsonPropertyName("progressBC")]
        public int[] ProgressBC { get; set; } = DefaultProgressBC.Clone() as int[];

        [JsonPropertyName("scrollSpeed")]
        public int ScrollSpeed { get; set; } = DefaultScrollSpeed;

        [JsonPropertyName("effect")]
        public string Effect { get; set; } = DefaultEffect;

        [JsonPropertyName("effectSpeed")]
        public int EffectSpeed { get; set; } = DefaultEffectSpeed;

        [JsonPropertyName("effectPalette")]
        public string EffectPalette { get; set; } = DefaultEffectPalette;

        [JsonPropertyName("effectBlend")]
        public bool EffectBlend { get; set; } = DefaultEffectBlend;
    }
}