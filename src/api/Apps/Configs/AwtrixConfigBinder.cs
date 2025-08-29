using System.Text.Json;
using AwtrixSharpWeb.Domain;
using Microsoft.Extensions.Configuration;

namespace AwtrixSharpWeb.Apps.Configs
{
    /// <summary>
    /// Custom configuration binder for AwtrixConfig that properly handles ValueMaps
    /// </summary>
    public static class AwtrixConfigBinder
    {
        /// <summary>
        /// Binds an AwtrixConfig from configuration and processes ValueMaps
        /// </summary>
        public static void BindAwtrixConfig(IServiceCollection services, IConfiguration configuration, JsonSerializerOptions jsonOptions)
        {
            // Register the standard IOptions<AwtrixConfig>
            services.Configure<AwtrixConfig>(configuration.GetSection("Awtrix"));
            
            // Add a post-configure step to process configuration and populate Keys and ValueMaps
            services.PostConfigure<AwtrixConfig>(config => 
            {
                if (config.Devices != null)
                {
                    foreach (var device in config.Devices)
                    {
                        if (device.Apps != null)
                        {
                            foreach (var appConfig in device.Apps)
                            {
                                // Process the app config properties from the configuration
                                ProcessAppConfig(appConfig, configuration, jsonOptions);
                            }
                        }
                    }
                }
            });
        }
        
        /// <summary>
        /// Process an AppConfig instance to populate Keys and ValueMaps
        /// </summary>
        private static void ProcessAppConfig(AppConfig appConfig, IConfiguration configuration, JsonSerializerOptions jsonOptions)
        {
            try
            {
                // Find the corresponding section in appsettings.json
                var appConfigSection = FindAppConfigSection(configuration, appConfig.Type);
                if (appConfigSection == null)
                {
                    return; // No matching section found
                }

                // Get all keys from the configuration section
                foreach (var child in appConfigSection.GetChildren())
                {
                    string key = child.Key;
                    string value = child.Value;
                    
                    // Skip null values
                    if (value == null) continue;
                    
                    // Add to the Keys dictionary
                    appConfig.SetConfig(key, value);
                }

                // Process ValueMaps if they exist
                ProcessValueMaps(appConfig, jsonOptions);
            }
            catch (Exception ex)
            {
                // Log error (we can't access the logger here, so just continue)
                System.Diagnostics.Debug.WriteLine($"Error processing AppConfig for {appConfig.Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Find the configuration section for a specific app type
        /// </summary>
        private static IConfigurationSection FindAppConfigSection(IConfiguration configuration, string appType)
        {
            // Typical path in appsettings.json: Awtrix:Devices:0:Apps:0
            // We need to search through devices and apps to find the right one by Type
            var awtrixSection = configuration.GetSection("Awtrix");
            var devicesSection = awtrixSection.GetSection("Devices");
            
            foreach (var deviceSection in devicesSection.GetChildren())
            {
                var appsSection = deviceSection.GetSection("Apps");
                
                foreach (var appSection in appsSection.GetChildren())
                {
                    string type = appSection["Type"];
                    if (type == appType)
                    {
                        return appSection;
                    }
                }
            }
            
            return null;
        }

        /// <summary>
        /// Process any ValueMaps entries that might be in the configuration JSON
        /// </summary>
        private static void ProcessValueMaps(AppConfig appConfig, JsonSerializerOptions jsonOptions)
        {
            try
            {
                // Check if this is a configuration that might have ValueMaps
                var valueMapsJson = appConfig.GetConfig<string>("ValueMaps");
                if (!string.IsNullOrEmpty(valueMapsJson))
                {
                    // Deserialize the ValueMaps JSON using our custom options
                    var valueMaps = JsonSerializer.Deserialize<List<ValueMap>>(valueMapsJson, jsonOptions);
                    if (valueMaps != null && valueMaps.Count > 0)
                    {
                        // Set the ValueMaps property directly
                        appConfig.ValueMaps = valueMaps;
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error (we can't access the logger here, so just continue)
                System.Diagnostics.Debug.WriteLine($"Error processing ValueMaps for {appConfig.Name}: {ex.Message}");
            }
        }
    }
}