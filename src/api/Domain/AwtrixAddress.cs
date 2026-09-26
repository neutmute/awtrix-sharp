namespace AwtrixSharpWeb.Domain
{

    public class AwtrixAddress
    {
        /// <summary>
        /// eg: "awtrix/clock1" (MQTT) or "http://192.168.1.50" (HTTP, device root, no /api suffix)
        /// </summary>
        public string BaseTopic { get; set; }

        /// <summary>
        /// Obsolete: every device is AWTRIX NG. Bound only so a leftover "Firmware" key can be reported at startup
        /// (ConfigurationWarnings); never read otherwise. See docs/config-migration.md.
        /// </summary>
        public string? Firmware { get; set; }

        /// <summary>
        /// True when BaseTopic addresses the device's HTTP API. Get-only, so ignored by configuration binding.
        /// </summary>
        public bool IsHttp => IsHttpTopic(BaseTopic);

        public static bool IsHttpTopic(string? topic)
        {
            return topic != null
                && (topic.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || topic.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        public override string ToString() => BaseTopic;
    }
}
