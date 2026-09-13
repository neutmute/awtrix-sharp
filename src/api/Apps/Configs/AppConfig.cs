using System.Globalization;
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
        /// Redirect for now. Reserved for future use if we want to differentiate between two apps of the same type on the same clock
        /// </summary>
        public string Name { get => Type; }

        /// <summary>
        /// Override values received based on a regex 
        /// </summary>
        public List<ValueMap> ValueMaps
        {
            get => _valueMaps;
            set => _valueMaps = value ?? new List<ValueMap>();
        }


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

        /// <summary>
        /// Parses the value with the invariant culture. A missing (or, for non-string types, whitespace) value
        /// returns default(T) rather than throwing (CR-23). A malformed value still throws; Validate() catches
        /// that at startup for the keys an app requires.
        /// </summary>
        public T GetConfig<T>(string key)
        {
            var converted = ConvertValue(Config.Get(key), typeof(T));
            return converted is null ? default! : (T)converted;
        }

        public void SetConfig<T>(string key, T value)
        {
            Config[key] = value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value?.ToString();
        }

        /// <summary>
        /// One entry per problem, each formatted "{Key}: {reason}". Empty when the config is valid.
        /// </summary>
        public virtual IReadOnlyList<string> Validate() => Array.Empty<string>();

        /// <summary>
        /// Throws <see cref="AppConfigValidationException"/> naming app type, device and keys when <see cref="Validate"/> reports problems.
        /// </summary>
        public void EnsureValid(string? device)
        {
            var errors = Validate();
            if (errors.Count > 0)
            {
                throw new AppConfigValidationException(Type, device, errors);
            }
        }

        protected void ValidateRequired(List<string> errors, string key)
        {
            if (string.IsNullOrWhiteSpace(Config.Get(key)))
            {
                errors.Add($"{key}: required value is missing");
            }
        }

        protected void ValidateTimeSpan(List<string> errors, string key, bool required, bool mustBePositive)
        {
            var raw = Config.Get(key);
            if (string.IsNullOrWhiteSpace(raw))
            {
                if (required)
                {
                    errors.Add($"{key}: required value is missing (expected hh:mm:ss)");
                }
                return;
            }

            if (!TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var value))
            {
                errors.Add($"{key}: '{raw}' is not a valid time span (expected hh:mm:ss)");
                return;
            }

            if (mustBePositive && value <= TimeSpan.Zero)
            {
                errors.Add($"{key}: '{raw}' must be greater than 00:00:00");
            }
            else if (!mustBePositive && value < TimeSpan.Zero)
            {
                errors.Add($"{key}: '{raw}' must not be negative");
            }
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
        /// Log every ValueMap configuration problem once, at Warning, naming the app type, device and map index.
        /// </summary>
        public void LogValueMapProblems(ILogger? logger, string? device)
        {
            if (logger == null || _valueMaps == null)
            {
                return;
            }

            for (var index = 0; index < _valueMaps.Count; index++)
            {
                var map = _valueMaps[index];
                if (map == null)
                {
                    continue;
                }

                foreach (var problem in map.GetConfigurationProblems())
                {
                    logger.LogWarning("{AppType} on {Device}: ValueMaps[{Index}]: {Problem}", Type, device, index, problem);
                }
            }
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
        /// Converts a string value to the specified type using the invariant culture (CR-23).
        /// </summary>
        private static object ConvertValue(string value, Type targetType)
        {
            if (value is null)
                return null;

            if (targetType == typeof(string))
                return value;

            if (string.IsNullOrWhiteSpace(value))
                return null;

            var invariant = CultureInfo.InvariantCulture;

            if (targetType == typeof(int) || targetType == typeof(int?))
                return int.Parse(value, invariant);

            if (targetType == typeof(long) || targetType == typeof(long?))
                return long.Parse(value, invariant);

            if (targetType == typeof(double) || targetType == typeof(double?))
                return double.Parse(value, invariant);

            if (targetType == typeof(decimal) || targetType == typeof(decimal?))
                return decimal.Parse(value, invariant);

            if (targetType == typeof(bool) || targetType == typeof(bool?))
                return bool.Parse(value);

            if (targetType == typeof(DateTime) || targetType == typeof(DateTime?))
                return DateTime.Parse(value, invariant);

            if (targetType == typeof(TimeSpan) || targetType == typeof(TimeSpan?))
                return TimeSpan.Parse(value, invariant);

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
