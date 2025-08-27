using System.Text.Json;
using AwtrixSharpWeb.Interfaces;

namespace AwtrixSharpWeb.Apps.Configs
{

    public class AppConfig : Dictionary<string, string>, IAppConfig
    {
        public const string EnvironmentKey = "Environment";
        private List<ValueMap> _valueMaps;

        public AppConfig()
        {
            _valueMaps = new List<ValueMap>();
        }

        public static AppConfig Empty(string environment = "")
        {
            // Tell the 
            var result = new AppConfig();
            result.SetEnvironment(environment);
            return result;
        }

        public AppConfig SetName(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                this["Name"] = name;
            }
            return this;
        }

        public string Get(string key)
        {
            if (TryGetValue(key, out var value))
            {
                return value;
            }
            return null;
        }

        /// <summary>
        /// Try and get, fall back to env var override
        /// </summary>
        public string Get(string key, string environmentVariable)
        {
            var value = Get(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                value = Environment.GetEnvironmentVariable(environmentVariable);
            }
            return value;
        }

        public string Name => Get("Name");

        /// <summary>
        /// Get the list of ValueMaps defined for this configuration
        /// </summary>
        public List<ValueMap> ValueMaps 
        { 
            get => _valueMaps;
            set => _valueMaps = value;
        }

        /// <summary>
        /// Find the first ValueMap that matches the input value
        /// </summary>
        /// <param name="input">The input string to match against ValueMatcher patterns</param>
        /// <returns>The first matching ValueMap or null if no match found</returns>
        public ValueMap FindMatchingValueMap(string input)
        {
            return _valueMaps?.FirstOrDefault(map => map.IsMatch(input));
        }

        /// <summary>
        /// Add a ValueMap to the collection
        /// </summary>
        public AppConfig AddValueMap(ValueMap valueMap)
        {
            if (valueMap != null && !string.IsNullOrEmpty(valueMap.ValueMatcher))
            {
                _valueMaps.Add(valueMap);
            }
            return this;
        }

        /// <summary>
        /// Add multiple ValueMaps to the collection
        /// </summary>
        public AppConfig AddValueMaps(IEnumerable<ValueMap> valueMaps)
        {
            if (valueMaps != null)
            {
                foreach (var map in valueMaps.Where(m => !string.IsNullOrEmpty(m.ValueMatcher)))
                {
                    _valueMaps.Add(map);
                }
            }
            return this;
        }

        /// <summary>
        /// Creates a new instance of the specified type and populates its properties from this AppConfig.
        /// </summary>
        /// <typeparam name="T">The type to convert to, must be a subclass of AppConfig</typeparam>
        /// <returns>A new instance of T with properties populated from the dictionary</returns>
        public T As<T>() where T : AppConfig, new()
        {
            return CreateFromAppConfig<T>(this);
        }

        /// <summary>
        /// Creates a new instance of the specified type and populates its properties from the source AppConfig.
        /// </summary>
        /// <typeparam name="T">The type to create, must be a subclass of AppConfig</typeparam>
        /// <param name="source">The source AppConfig containing the values to map</param>
        /// <returns>A new instance of T with properties populated from the source dictionary</returns>
        public static T CreateFromAppConfig<T>(AppConfig source) where T : AppConfig, new()
        {
            // Create a new instance of the target type
            T target = new T();

            // Copy all key-value pairs from source to target
            foreach (var kvp in source)
            {
                target[kvp.Key] = kvp.Value;
            }

            // Copy ValueMaps if present
            if (source._valueMaps != null && source._valueMaps.Count > 0)
            {
                target.ValueMaps = new List<ValueMap>(source._valueMaps);
            }

            // Get all properties of the target type that can be written to
            var properties = typeof(T).GetProperties()
                .Where(p => p.CanWrite && p.Name != "Item" && p.Name != "Keys" && p.Name != "Values" && p.Name != "ValueMaps")
                .ToList();

            foreach (var property in properties)
            {
                // Try to get the value from the dictionary
                string key = property.Name;
                if (source.TryGetValue(key, out string stringValue) && !string.IsNullOrEmpty(stringValue))
                {
                    // Convert the string value to the property's type and set it
                    try
                    {
                        object convertedValue = ConvertValue(stringValue, property.PropertyType);
                        if (convertedValue != null)
                        {
                            property.SetValue(target, convertedValue);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log or handle conversion errors
                        System.Diagnostics.Debug.WriteLine($"Error converting value '{stringValue}' to type {property.PropertyType} for property {property.Name}: {ex.Message}");
                    }
                }
            }

            return target;
        }

        /// <summary>
        /// Converts a string value to the specified type.
        /// </summary>
        private static object ConvertValue(string value, Type targetType)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            if (targetType == typeof(string))
                return value;

            if (targetType == typeof(int) || targetType == typeof(int?))
                return int.Parse(value);

            if (targetType == typeof(long) || targetType == typeof(long?))
                return long.Parse(value);

            if (targetType == typeof(double) || targetType == typeof(double?))
                return double.Parse(value);

            if (targetType == typeof(decimal) || targetType == typeof(decimal?))
                return decimal.Parse(value);

            if (targetType == typeof(bool) || targetType == typeof(bool?))
                return bool.Parse(value);

            if (targetType == typeof(DateTime) || targetType == typeof(DateTime?))
                return DateTime.Parse(value);

            if (targetType == typeof(TimeSpan) || targetType == typeof(TimeSpan?))
                return TimeSpan.Parse(value);

            if (targetType == typeof(Guid) || targetType == typeof(Guid?))
                return Guid.Parse(value);

            if (targetType.IsEnum)
                return Enum.Parse(targetType, value, ignoreCase: true);

            if (targetType == typeof(List<ValueMap>))
            {
                try
                {
                    return JsonSerializer.Deserialize<List<ValueMap>>(value);
                }
                catch
                {
                    return new List<ValueMap>();
                }
            }

            // Add more type conversions as needed

            // For complex types, you might want to use JSON deserialization or other methods
            throw new NotSupportedException($"Conversion from string to {targetType} is not supported.");
        }

        public void SetEnvironment(string environment)
        {
            if (!string.IsNullOrWhiteSpace(environment))
            {
                this[EnvironmentKey] = environment;
            }
        }

        public override string ToString()
        {
            return string.Join(
                "; ",
                this.OrderBy(kvp => kvp.Key == "Name" ? "" : kvp.Key)       // always name first
                    .Select(kvp => $"{kvp.Key}={kvp.Value}")
            );
        }
    }
}
