using System.Text.Json;
using System.Text.Json.Serialization;
using AwtrixSharpWeb.Interfaces;

namespace AwtrixSharpWeb.Apps.Configs
{

    public class AppConfig : IAppConfig
    {
        private List<ValueMap> _valueMaps;

        public AppConfigKeys Config { get; set; }

        [JsonIgnore]
        public string Environment { get; set; }

        public string Type { get; set; }

        /// <summary>
        /// Redirect for now
        /// </summary>
        public string Name { get => Type; }

        public AppConfig()
        {
            Config = new AppConfigKeys();
            _valueMaps = new List<ValueMap>();
        }

        public static AppConfig Empty(string environment = "")
        {
            // Tell the 
            var result = new AppConfig();
            result.Environment = environment;
            return result;
        }

        public AppConfig WithName(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                Type = name;
            }
            return this;
        }

        
        public T GetConfig<T>(string key)
        {
            return (T)ConvertValue(Config.Get(key), typeof(T));
        }

        public void SetConfig<T>(string key, T value)
        {
            if (Config.ContainsKey(key))
            {
                Config[key] = value?.ToString();
            }
            else
            {
                Config.Add(key, value?.ToString());
            }
        }

        /// <summary>
        /// Get the list of ValueMaps defined for this configuration
        /// </summary>
        public List<ValueMap> ValueMaps 
        { 
            get => _valueMaps;
            set => _valueMaps = value ?? new List<ValueMap>();
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

            target.Config = source.Config.Clone();
            target.Environment = source.Environment;    
            target.Type = source.Type;

            // Copy ValueMaps if present
            if (source._valueMaps != null && source._valueMaps.Count > 0)
            {
                target.ValueMaps = new List<ValueMap>(source._valueMaps);
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
       
        public override string ToString()
        {
            return $"{Type}, Config={Config.ToString()}";
        }
    }
}
