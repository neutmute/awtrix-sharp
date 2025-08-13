using System.Text.Json.Serialization;

namespace AwtrixSharpWeb.Domain
{
    /// <summary>
    /// Dictionary-based implementation of an Awtrix application message
    /// that stores all properties as string key-value pairs without default values.
    /// </summary>
    public class AwtrixAppMessage2 : Dictionary<string, string>
    {
        // Helper methods to set properties with proper key names matching the original class

        public AwtrixAppMessage2 SetText(string value)
        {
            this["text"] = value;
            return this;
        }

        public AwtrixAppMessage2 SetTextCase(int value)
        {
            this["textCase"] = value.ToString();
            return this;
        }

        public AwtrixAppMessage2 SetTopText(bool value)
        {
            this["topText"] = value.ToString().ToLower();
            return this;
        }

        public AwtrixAppMessage2 SetTextOffset(int value)
        {
            this["textOffset"] = value.ToString();
            return this;
        }

        public AwtrixAppMessage2 SetCenter(bool value)
        {
            this["center"] = value.ToString().ToLower();
            return this;
        }

        public AwtrixAppMessage2 SetColor(int[] value)
        {
            this["color"] = string.Join(',', value);
            return this;
        }

        public AwtrixAppMessage2 SetGradient(int[][] value)
        {
            if (value != null && value.Length > 0)
            {
                this["gradient"] = string.Join(';', value.Select(arr => string.Join(',', arr)));
            }
            return this;
        }

        public AwtrixAppMessage2 SetBlinkText(double value)
        {
            this["blinkText"] = value.ToString();
            return this;
        }

        public AwtrixAppMessage2 SetFadeText(double value)
        {
            this["fadeText"] = value.ToString();
            return this;
        }

        public AwtrixAppMessage2 SetBackground(int[] value)
        {
            this["background"] = string.Join(',', value);
            return this;
        }

        public AwtrixAppMessage2 SetRainbow(bool value)
        {
            this["rainbow"] = value.ToString().ToLower();
            return this;
        }

        public AwtrixAppMessage2 SetIcon(string value)
        {
            this["icon"] = value;
            return this;
        }

        public AwtrixAppMessage2 SetPushIcon(int value)
        {
            this["pushIcon"] = value.ToString();
            return this;
        }

        public AwtrixAppMessage2 SetDuration(int value)
        {
            this["duration"] = value.ToString();
            return this;
        }

        public AwtrixAppMessage2 SetLine(int[] value)
        {
            this["line"] = string.Join(',', value);
            return this;
        }

        public AwtrixAppMessage2 SetLifetime(int value)
        {
            this["lifetime"] = value.ToString();
            return this;
        }

        public AwtrixAppMessage2 SetLifetimeMode(int value)
        {
            this["lifetimeMode"] = value.ToString();
            return this;
        }

        public AwtrixAppMessage2 SetBar(int[] value)
        {
            this["bar"] = string.Join(',', value);
            return this;
        }

        public AwtrixAppMessage2 SetAutoscale(bool value)
        {
            this["autoscale"] = value.ToString().ToLower();
            return this;
        }

        public AwtrixAppMessage2 SetOverlay(string value)
        {
            this["overlay"] = value;
            return this;
        }

        public AwtrixAppMessage2 SetProgress(int value)
        {
            this["progress"] = value.ToString();
            return this;
        }

        public AwtrixAppMessage2 SetProgressC(int[] value)
        {
            this["progressC"] = string.Join(',', value);
            return this;
        }

        public AwtrixAppMessage2 SetProgressBC(int[] value)
        {
            this["progressBC"] = string.Join(',', value);
            return this;
        }

        public AwtrixAppMessage2 SetScrollSpeed(int value)
        {
            this["scrollSpeed"] = value.ToString();
            return this;
        }

        public AwtrixAppMessage2 SetEffect(string value)
        {
            this["effect"] = value;
            return this;
        }

        public AwtrixAppMessage2 SetEffectSpeed(int value)
        {
            this["effectSpeed"] = value.ToString();
            return this;
        }

        public AwtrixAppMessage2 SetEffectPalette(string value)
        {
            this["effectPalette"] = value;
            return this;
        }

        public AwtrixAppMessage2 SetEffectBlend(bool value)
        {
            this["effectBlend"] = value.ToString().ToLower();
            return this;
        }
    }
}