using AwtrixSharpWeb.Interfaces;

namespace AwtrixSharpWeb.Domain
{
    public static class AppConfigExtensions
    {
        public static void SetEnvironment(this AppConfig config, string environment)
        {
            if (!string.IsNullOrWhiteSpace(environment))
            {
                config.TryAdd(AppConfig.EnvironmentKey, environment);
            }
        }

        public static bool IsEnvironmentDev(this AppConfig config)
        {
            if (config.TryGetValue(AppConfig.EnvironmentKey, out var environment))
            { 
                return environment.ToLower().StartsWith("dev");
            }
            return false;
        }
    }

    public class AppConfig : Dictionary<string, string>, IAppConfig
    {
        public const string EnvironmentKey = "Environment";

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
            if (this.TryGetValue(key, out var value))
            {
                return value;
            }
            return null;
        }

        public string Name => Get("Name");

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

            // Get all properties of the target type that can be written to
            var properties = typeof(T).GetProperties()
                .Where(p => p.CanWrite && p.Name != "Item" && p.Name != "Keys" && p.Name != "Values")
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

            // Add more type conversions as needed

            // For complex types, you might want to use JSON deserialization or other methods
            throw new NotSupportedException($"Conversion from string to {targetType} is not supported.");
        }
    }

    public class ScheduledAppConfig : AppConfig
    {
        public string CronSchedule { get; set; }

        /// <summary>
        /// How long to take over the clock for
        /// </summary>
        public TimeSpan ActiveTime { get; set; }
    }

    public class TripTimerAppConfig : ScheduledAppConfig 
    {

        public string StopIdOrigin { get; set; } = string.Empty;

        public string StopIdDestination { get; set; } = string.Empty;


        /// <summary>
        /// Travel time to get to origin
        /// </summary>
        public TimeSpan TimeToOrigin { get; set; }

        /// <summary>
        /// How much time to get ready before leaving
        /// </summary>
        public TimeSpan  TimeToPrepare { get; set; }
    }
}
