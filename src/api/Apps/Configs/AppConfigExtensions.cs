namespace AwtrixSharpWeb.Apps.Configs
{
    public static class AppConfigExtensions
    {
        public static void SetEnvironment(this AppConfig config, string environment)
        {
            if (!string.IsNullOrWhiteSpace(environment))
            {
                config.Environment = environment;
            }
        }

        public static bool IsEnvironmentDev(this AppConfig config)
        {
            return config.Environment.ToLower().StartsWith("dev");
        }
    }
}
