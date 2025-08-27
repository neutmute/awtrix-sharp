using System.Text.Json.Serialization;

namespace AwtrixSharpWeb.Domain
{

    /// <summary>
    /// Dictionary-based implementation of an Awtrix application message
    /// that stores all properties as string key-value pairs without default values.
    /// </summary>
    public class AwtrixAppMessage : Dictionary<string, string>
    {
        // Helper methods to set properties with proper key names matching the original class

        private string Get(string key)
        {
            if (this.TryGetValue(key, out var value))
            {
                return value;
            }
            return null;
        }

        public string Text => Get("text");



        public AwtrixAppMessage SetText(string value)
        {
            this["text"] = value;
            return this;
        }

        public AwtrixAppMessage SetTextCase(int value)
        {
            this["textCase"] = value.ToString();
            return this;
        }

        public AwtrixAppMessage SetTopText(bool value)
        {
            return Set("topText", value);
        }


        public AwtrixAppMessage SetHold(bool value = true)
        {
            return Set("hold", value);
        }

        public AwtrixAppMessage SetStack(bool value = true)
        {
            return Set("stack", value);
        }


        public AwtrixAppMessage SetTextOffset(int value)
        {
            this["textOffset"] = value.ToString();
            return this;
        }

        public AwtrixAppMessage SetCenter(bool value)
        {
            return Set("center", value);
        }
        public AwtrixAppMessage SetColor(string value)
        {
            this["color"] = value;
            return this;
        }

        public AwtrixAppMessage SetColor(int[] value)
        {
            this["color"] = string.Join(',', value);
            return this;
        }

        public AwtrixAppMessage SetGradient(int[][] value)
        {
            if (value != null && value.Length > 0)
            {
                this["gradient"] = string.Join(';', value.Select(arr => string.Join(',', arr)));
            }
            return this;
        }

        public AwtrixAppMessage SetBlinkText(double value)
        {
            this["blinkText"] = value.ToString();
            return this;
        }

        public AwtrixAppMessage SetFadeText(double value)
        {
            this["fadeText"] = value.ToString();
            return this;
        }

        public AwtrixAppMessage SetBackground(int[] value)
        {
            this["background"] = string.Join(',', value);
            return this;
        }

        public AwtrixAppMessage SetRainbow(bool value = true)
        {
            return Set("rainbow", value);
        }

        public AwtrixAppMessage SetIcon(string value)
        {
            this["icon"] = value;
            return this;
        }

        public AwtrixAppMessage SetPushIcon(int value)
        {
            this["pushIcon"] = value.ToString();
            return this;
        }

        public AwtrixAppMessage SetDuration(int value)
        {
            SetDuration(TimeSpan.FromSeconds(value));
            return this;
        }

        public AwtrixAppMessage SetDuration(TimeSpan value)
        {
            this["duration"] = Convert.ToInt32(value.TotalSeconds).ToString();
            return this;
        }

        public AwtrixAppMessage SetLine(int[] value)
        {
            this["line"] = string.Join(',', value);
            return this;
        }

        public AwtrixAppMessage SetLifetime(int value)
        {
            this["lifetime"] = value.ToString();
            return this;
        }

        public AwtrixAppMessage SetLifetimeMode(int value)
        {
            this["lifetimeMode"] = value.ToString();
            return this;
        }

        public AwtrixAppMessage SetBar(int[] value)
        {
            this["bar"] = string.Join(',', value);
            return this;
        }

        public AwtrixAppMessage SetAutoscale(bool value) => Set("autoscale", value);

        public AwtrixAppMessage SetOverlay(string value)
        {
            this["overlay"] = value;
            return this;
        }

        public AwtrixAppMessage SetProgress(int value)
        {
            this["progress"] = value.ToString();
            return this;
        }

        public AwtrixAppMessage SetProgressC(int[] value)
        {
            this["progressC"] = string.Join(',', value);
            return this;
        }

        public AwtrixAppMessage SetProgressBC(int[] value)
        {
            this["progressBC"] = string.Join(',', value);
            return this;
        }

        public AwtrixAppMessage SetScrollSpeed(int value)
        {
            this["scrollSpeed"] = value.ToString();
            return this;
        }

        public AwtrixAppMessage SetEffect(string value)
        {
            this["effect"] = value;
            return this;
        }

        public AwtrixAppMessage SetEffectSpeed(int value)
        {
            this["effectSpeed"] = value.ToString();
            return this;
        }

        public AwtrixAppMessage SetEffectPalette(string value)
        {
            this["effectPalette"] = value;
            return this;
        }

        public AwtrixAppMessage SetEffectBlend(bool value) => Set("effectBlend", value);


        private AwtrixAppMessage Set(string key, bool value)
        {
            this[key] = value.ToString().ToLower();
            return this;
        }

        public override string ToString()
        {
            return string.Join(
                "; ",
                this.OrderBy(kvp => kvp.Key == "Text" ? "" : kvp.Key)       // always name first
                    .Select(kvp => $"{kvp.Key}={kvp.Value}")
            );
        }

        public string ToJson()
        {
            // Create a new dictionary to modify if needed
            var dictionaryToSerialize = new Dictionary<string, object>(this.Count);
            
            // Copy all items from this dictionary to the new one
            foreach (var kvp in this)
            {
                // Check if it's the text property and if it starts with "[
                // In which case its an encoded JSON object
                if (kvp.Key == "text" && kvp.Value != null && kvp.Value.StartsWith("["))
                {
                    try
                    {
                        // Try to parse the text as JSON
                        var jsonElement = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(kvp.Value);
                        dictionaryToSerialize[kvp.Key] = jsonElement; // Add as JSON object
                    }
                    catch
                    {
                        // If parsing fails, use the original string
                        dictionaryToSerialize[kvp.Key] = kvp.Value;
                    }
                }
                else
                {
                    // Add other properties as is
                    dictionaryToSerialize[kvp.Key] = kvp.Value;
                }
            }
            
            // Serialize the modified dictionary
            var json = System.Text.Json.JsonSerializer.Serialize(dictionaryToSerialize);
            return json;
        }
    }
}