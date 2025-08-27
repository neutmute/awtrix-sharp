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

        public bool IsMatch(string input)
        {
            if (string.IsNullOrEmpty(ValueMatcher))
                return false;

            try
            {
                return Regex.IsMatch(input, ValueMatcher);
            }
            catch
            {
                // Fallback to string comparison if regex is invalid
                return input.Contains(ValueMatcher);
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

            // Apply mapped values from the ValueMap
            foreach (var kvp in this)
            {
                if (kvp.Key == "ValueMatcher")
                    continue; // Skip the matcher itself

                // Set properties based on the value map
                switch (kvp.Key)
                {
                    case "Icon":
                        message.SetIcon(kvp.Value);
                        break;
                    case "Text":
                        message.SetText(kvp.Value);
                        break;
                    case "Color":
                        if (TryParseColorArray(kvp.Value, out int[] colorArray))
                            message.SetColor(colorArray);
                        break;
                    // Add more property mappings as needed
                    default:
                        // For any other property, try to apply it via reflection
                        ApplyDynamicProperty(message, kvp.Key, kvp.Value, logger);
                        break;
                }
            }
        }

        private void ApplyDynamicProperty(AwtrixAppMessage message, string propertyName, string value, ILogger logger)
        {
            try
            {
                // Try to find a matching "Set" method on AwtrixAppMessage
                var methodName = "Set" + propertyName;
                var method = typeof(AwtrixAppMessage).GetMethod(methodName);

                if (method != null)
                {
                    // Determine parameter type and convert value
                    var parameters = method.GetParameters();
                    if (parameters.Length == 1)
                    {
                        var paramType = parameters[0].ParameterType;
                        object convertedValue = null;

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
                        // Add more type conversions as needed

                        if (convertedValue != null)
                        {
                            method.Invoke(message, new[] { convertedValue });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw;
                logger.LogWarning("Failed to apply property {PropertyName}: {ErrorMessage}", propertyName, ex.Message);
            }
        }
    }
}
