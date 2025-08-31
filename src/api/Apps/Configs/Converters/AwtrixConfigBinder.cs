//using System.Text.Json;
//using AwtrixSharpWeb.Domain;
//using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.Logging;

//namespace AwtrixSharpWeb.Apps.Configs
//{
//    /// <summary>
//    /// Custom configuration binder for AwtrixConfig that properly handles ValueMaps
//    /// </summary>
//    public static class AwtrixConfigBinder
//    {
//        /// <summary>
//        /// Binds an AwtrixConfig from configuration and processes ValueMaps
//        /// </summary>
//        public static void BindAwtrixConfig(IServiceCollection services, IConfiguration configuration, JsonSerializerOptions jsonOptions)
//        {
//            // Ensure our custom converters are registered
//            EnsureConvertersRegistered(jsonOptions);
            
//            // Register the standard IOptions<AwtrixConfig>
//            services.Configure<AwtrixConfig>(configuration.GetSection("Awtrix"));
            
//            // Add a post-configure step to process configuration and populate Config and ValueMaps
//            services.PostConfigure<AwtrixConfig>(config => 
//            {
//                if (config.Devices != null)
//                {
//                    foreach (var device in config.Devices)
//                    {
//                        if (device.Apps != null)
//                        {
//                            foreach (var appConfig in device.Apps)
//                            {
//                                // Process the app config properties from the configuration
//                                ProcessAppConfig(appConfig, configuration, jsonOptions);
//                            }
//                        }
//                    }
//                }
//            });
//        }

//        /// <summary>
//        /// Ensures all required JSON converters are registered
//        /// </summary>
//        private static void EnsureConvertersRegistered(JsonSerializerOptions jsonOptions)
//        {
//            if (!jsonOptions.Converters.Any(c => c is AppConfigKeysJsonConverter))
//            {
//                jsonOptions.Converters.Add(new AppConfigKeysJsonConverter());
//            }
            
//            if (!jsonOptions.Converters.Any(c => c is AppConfigJsonConverter))
//            {
//                jsonOptions.Converters.Add(new AppConfigJsonConverter());
//            }
            
//            if (!jsonOptions.Converters.Any(c => c is ValueMapJsonConverter))
//            {
//                jsonOptions.Converters.Add(new ValueMapJsonConverter());
//            }
//        }
        
//        /// <summary>
//        /// Process an AppConfig instance to populate Config and ValueMaps
//        /// </summary>
//        private static void ProcessAppConfig(AppConfig appConfig, IConfiguration configuration, JsonSerializerOptions jsonOptions)
//        {
//            try
//            {
//                // Find the corresponding section in appsettings.json
//                var appConfigSection = FindAppConfigSection(configuration, appConfig.Type);
//                if (appConfigSection == null)
//                {
//                    System.Diagnostics.Debug.WriteLine($"No configuration section found for app type: {appConfig.Type}");
//                    return; // No matching section found
//                }

//                // Get the JSON representation of the configuration section
//                var appConfigJson = GetSectionAsJson(appConfigSection);
//                if (string.IsNullOrEmpty(appConfigJson))
//                {
//                    System.Diagnostics.Debug.WriteLine($"Failed to get JSON for app type: {appConfig.Type}");
//                    return;
//                }

//                // Deserialize directly to AppConfig using our custom converter
//                var deserializedConfig = JsonSerializer.Deserialize<AppConfig>(appConfigJson, jsonOptions);
//                if (deserializedConfig == null)
//                {
//                    System.Diagnostics.Debug.WriteLine($"Failed to deserialize config for app type: {appConfig.Type}");
//                    return;
//                }

//                // Copy properties from deserialized config to our app config
//                CopyAppConfigProperties(deserializedConfig, appConfig);
                
//                // Log successful processing
//                System.Diagnostics.Debug.WriteLine($"Processed AppConfig for {appConfig.Name} with {appConfig.Config?.Count ?? 0} config entries");
//            }
//            catch (Exception ex)
//            {
//                // Log error (we can't access the logger here, so just continue)
//                System.Diagnostics.Debug.WriteLine($"Error processing AppConfig for {appConfig.Name}: {ex.Message}");
//                System.Diagnostics.Debug.WriteLine(ex.StackTrace);
//            }
//        }

//        /// <summary>
//        /// Copy properties from source AppConfig to target AppConfig
//        /// </summary>
//        private static void CopyAppConfigProperties(AppConfig source, AppConfig target)
//        {
//            // Copy Environment if not already set
//            if (string.IsNullOrEmpty(target.Environment) && !string.IsNullOrEmpty(source.Environment))
//            {
//                target.Environment = source.Environment;
//            }

//            // Copy Config entries (we're now handling the Config as a whole object)
//            if (source.Config != null && source.Config.Count > 0)
//            {
//                // Replace the entire Config object for cleaner handling
//                target.Config = source.Config.Clone();
//            }

//            // Copy ValueMaps
//            if (source.ValueMaps != null && source.ValueMaps.Count > 0)
//            {
//                target.ValueMaps = new List<ValueMap>(source.ValueMaps);
//            }
//        }

//        /// <summary>
//        /// Find the configuration section for a specific app type
//        /// </summary>
//        private static IConfigurationSection FindAppConfigSection(IConfiguration configuration, string appType)
//        {
//            // Typical path in appsettings.json: Awtrix:Devices:0:Apps:0
//            // We need to search through devices and apps to find the right one by Type
//            var awtrixSection = configuration.GetSection("Awtrix");
//            var devicesSection = awtrixSection.GetSection("Devices");
            
//            foreach (var deviceSection in devicesSection.GetChildren())
//            {
//                var appsSection = deviceSection.GetSection("Apps");
                
//                foreach (var appSection in appsSection.GetChildren())
//                {
//                    string type = appSection["Type"];
//                    if (type == appType)
//                    {
//                        return appSection;
//                    }
//                }
//            }
            
//            return null;
//        }

//        /// <summary>
//        /// Get a configuration section as a JSON string
//        /// </summary>
//        private static string GetSectionAsJson(IConfigurationSection section)
//        {
//            try
//            {
//                // Create a dictionary to hold the configuration values
//                var dict = new Dictionary<string, object>();
                
//                // Process all children recursively
//                foreach (var child in section.GetChildren())
//                {
//                    if (child.Value != null)
//                    {
//                        // Simple key-value pair
//                        dict[child.Key] = child.Value;
//                    }
//                    else
//                    {
//                        // Complex object, process recursively
//                        var childDict = ProcessConfigSection(child);
//                        if (childDict != null)
//                        {
//                            dict[child.Key] = childDict;
//                        }
//                    }
//                }
                
//                // Serialize the dictionary to JSON
//                return JsonSerializer.Serialize(dict);
//            }
//            catch (Exception ex)
//            {
//                System.Diagnostics.Debug.WriteLine($"Error getting section as JSON: {ex.Message}");
//                return null;
//            }
//        }

//        /// <summary>
//        /// Process a configuration section into a dictionary
//        /// </summary>
//        private static object ProcessConfigSection(IConfigurationSection section)
//        {
//            var children = section.GetChildren().ToList();
            
//            // If no children, return null
//            if (!children.Any())
//            {
//                return null;
//            }
            
//            // Check if this is an array
//            if (children.All(c => int.TryParse(c.Key, out _)))
//            {
//                // This is an array
//                var array = new List<object>();
//                foreach (var child in children.OrderBy(c => int.Parse(c.Key)))
//                {
//                    if (child.Value != null)
//                    {
//                        array.Add(child.Value);
//                    }
//                    else
//                    {
//                        var processed = ProcessConfigSection(child);
//                        if (processed != null)
//                        {
//                            array.Add(processed);
//                        }
//                    }
//                }
//                return array.Count > 0 ? array : null;
//            }
//            else
//            {
//                // This is an object
//                var dict = new Dictionary<string, object>();
//                foreach (var child in children)
//                {
//                    if (child.Value != null)
//                    {
//                        dict[child.Key] = child.Value;
//                    }
//                    else
//                    {
//                        var processed = ProcessConfigSection(child);
//                        if (processed != null)
//                        {
//                            dict[child.Key] = processed;
//                        }
//                    }
//                }
//                return dict.Count > 0 ? dict : null;
//            }
//        }
//    }
//}