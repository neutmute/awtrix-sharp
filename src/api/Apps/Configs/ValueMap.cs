using AwtrixSharpWeb.Domain;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace AwtrixSharpWeb.Apps.Configs
{
    public class ValueMap : Dictionary<string, string>
    {
        public string ValueMatcher
        {
            get => this.TryGetValue("ValueMatcher", out var value) ? value : string.Empty;
            set => this["ValueMatcher"] = value;
        }

        public ValueMap Clone()
        {
            var clone = new ValueMap();
            foreach (var key in this.Keys)
            {
                clone.Add(key, this[key]);
            }
            ;
            return clone;
        }

        public bool IsMatch(string input)
        {
            if (string.IsNullOrEmpty(ValueMatcher) || string.IsNullOrEmpty(input))
                return false;

            try
            {
                return Regex.IsMatch(input, ValueMatcher, RegexOptions.IgnoreCase);
            }
            catch
            {
                // Fallback to string comparison if regex is invalid
                return input.Contains(ValueMatcher, StringComparison.OrdinalIgnoreCase);
            }
        }


        private bool TryParseColorArray(string value, out int[] colorArray)
        {
            try
            {
                var parts = value.Split(',');
                if (parts.Length >= 3)
                {
                    colorArray = parts.Select(int.Parse).ToArray();
                    return true;
                }
            }
            catch
            {
                // Parse error, fallback to default
            }

            colorArray = new int[] { 255, 255, 255 };
            return false;
        }

        public void Decorate(AwtrixAppMessage message, ILogger logger)
        {
            try
            {
                // Apply mapped values from the ValueMap
                foreach (var kvp in this)
                {
                    if (kvp.Key == "ValueMatcher")
                        continue; // Skip the matcher itself

                    // Process property names case-insensitively
                    var propertyName = kvp.Key;
                    
                    // Set properties based on the value map
                    switch (propertyName.ToLowerInvariant())
                    {
                        case "text":
                            message.SetText(kvp.Value);
                            break;
                        case "icon":
                            message.SetIcon(kvp.Value);
                            break;
                        case "color":
                            if (TryParseColorArray(kvp.Value, out int[] colorArray))
                                message.SetColor(colorArray);
                            else
                                message.SetColor(kvp.Value);
                            break;
                        case "duration":
                            // Handle Duration specifically to avoid ambiguity
                            if (int.TryParse(kvp.Value, out int durationSeconds))
                                message.SetDuration(durationSeconds);
                            break;
                        case "center":
                            if (bool.TryParse(kvp.Value, out bool centerValue))
                                message.SetCenter(centerValue);
                            break;
                        // Add more property mappings as needed
                        default:
                            // For any other property, try to apply it via reflection
                            ApplyDynamicProperty(message, propertyName, kvp.Value, logger);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Error in Decorate: {Error}", ex.Message);
            }
        }

        public override string ToString()
        {
            return $"ValueMatcher={ValueMatcher}";
        }

        private void ApplyDynamicProperty(AwtrixAppMessage message, string propertyName, string value, ILogger logger)
        {
            try
            {
                // Try to find a matching "Set" method on AwtrixAppMessage
                var methodName = "Set" + propertyName;
                
                // Look for method with exact parameter type match to avoid ambiguity
                var methods = typeof(AwtrixAppMessage).GetMethods()
                    .Where(m => m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase) && 
                           m.GetParameters().Length == 1)
                    .ToList();
                
                if (methods.Count == 0)
                    return;
                
                // Try to find the best matching method
                foreach (var method in methods)
                {
                    var paramType = method.GetParameters()[0].ParameterType;
                    object convertedValue = null;

                    try
                    {
                        if (paramType == typeof(string))
                        {
                            convertedValue = value;
                        }
                        else if (paramType == typeof(bool))
                        {
                            convertedValue = bool.Parse(value);
                        }
                        else if (paramType == typeof(int))
                        {
                            convertedValue = int.Parse(value);
                        }
                        else if (paramType == typeof(int[]))
                        {
                            if (TryParseColorArray(value, out int[] array))
                                convertedValue = array;
                        }
                        
                        if (convertedValue != null)
                        {
                            method.Invoke(message, new[] { convertedValue });
                            return; // Successfully applied
                        }
                    }
                    catch
                    {
                        // Try the next method if conversion fails
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Failed to apply property {PropertyName}: {ErrorMessage}", propertyName, ex.Message);
            }
        }
    }
}
