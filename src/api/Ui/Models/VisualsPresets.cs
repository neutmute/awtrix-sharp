namespace AwtrixSharpWeb.Ui.Models
{
    /// <summary>Built-in starting points for the visuals playground. Each mutates a freshly reset form.</summary>
    public static class VisualsPresets
    {
        public sealed record Preset(string Name, Action<VisualsForm> Apply);

        public static readonly IReadOnlyList<Preset> All = new[]
        {
            new Preset("Plasma + Rainbow", f => { f.Text = "Plasma"; f.Effect = "Plasma"; f.Palette = "Rainbow"; f.DurationMs = 8000; }),
            new Preset("Matrix green", f => { f.Text = "Matrix"; f.TextColor = "#00FF00"; f.Effect = "Matrix"; f.EffectSpeed = 1.5; f.DurationMs = 8000; }),
            new Preset("Snow overlay, hold", f => { f.Text = "Snow"; f.Overlay = "snow"; f.Hold = true; }),
            new Preset("Fireworks, fast", f => { f.Text = "Boom"; f.Effect = "Fireworks"; f.EffectSpeed = 3; f.Palette = "Party"; f.DurationMs = 6000; }),
            new Preset("Palette text bounce", f => { f.Text = "Bouncing palette text"; f.TextColor = "#FFFFFF"; f.Palette = "Ocean"; f.ScrollMode = "bounce"; f.ScrollSpeed = 60; f.DurationMs = 10000; }),
        };
    }
}
