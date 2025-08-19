namespace AwtrixSharpWeb.Apps.Configs
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
}
