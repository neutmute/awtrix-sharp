namespace AwtrixSharpWeb.Apps.Configs
{
    /// <summary>
    /// Thrown by <see cref="AppConfig.EnsureValid"/>. The message names the app type, device and every invalid key,
    /// so Conductor's per-app guard can log it as the skip reason (CR-23).
    /// </summary>
    public class AppConfigValidationException : Exception
    {
        public AppConfigValidationException(string? appType, string? device, IReadOnlyList<string> errors)
            : base($"Invalid configuration for app '{appType}' on device '{device}': {string.Join("; ", errors)}")
        {
            AppType = appType;
            Device = device;
            Errors = errors;
        }

        public string? AppType { get; }

        public string? Device { get; }

        public IReadOnlyList<string> Errors { get; }
    }
}
