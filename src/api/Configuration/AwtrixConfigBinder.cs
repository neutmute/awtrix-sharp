//using System.Text.Json;
//using AwtrixSharpWeb.Apps.Configs;
//using Microsoft.Extensions.Configuration;

//namespace AwtrixSharpWeb.Configuration
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
//            // Register the standard IOptions<AwtrixConfig>
//            services.Configure<Domain.AwtrixConfig>(configuration.GetSection("Awtrix"));
            
//            // Add a post-configure step to process ValueMaps
//            services.PostConfigure<Domain.AwtrixConfig>(config => 
//            {
//                if (config.Devices != null)
//                {
//                    foreach (var device in config.Devices)
//                    {
//                        if (device.Apps != null)
//                        {
//                            foreach (var appConfig in device.Apps)
//                            {
//                                ProcessValueMaps(appConfig, jsonOptions);
//                            }
//                        }
//                    }
//                }
//            });
//        }
        
//        /// <summary>
//        /// Process any ValueMaps entries that might be in the configuration JSON
//        /// </summary>
//        private static void ProcessValueMaps(AppConfig appConfig, JsonSerializerOptions jsonOptions)
//        {
//            try
//            {
//                // Check if this is a configuration that might have ValueMaps
//                if (appConfig.Keys.TryGetValue("ValueMaps", out string valueMapsJson))
//                {
//                    if (!string.IsNullOrEmpty(valueMapsJson))
//                    {
//                        // Deserialize the ValueMaps JSON using our custom options
//                        var valueMaps = JsonSerializer.Deserialize<List<ValueMap>>(valueMapsJson, jsonOptions);
//                        if (valueMaps != null && valueMaps.Count > 0)
//                        {
//                            // Add ValueMaps to the AppConfig
//                            appConfig.AddValueMaps(valueMaps);
//                        }
//                    }
//                }
//            }
//            catch (Exception ex)
//            {
//                // Log error (we can't access the logger here, so just continue)
//                System.Diagnostics.Debug.WriteLine($"Error processing ValueMaps for {appConfig.Name}: {ex.Message}");
//            }
//        }
//    }
//}