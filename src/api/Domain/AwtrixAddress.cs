namespace AwtrixSharpWeb.Domain
{
    public class AwtrixConfig
    {
        public DeviceConfig[] Devices { get; set; } = Array.Empty<DeviceConfig>();
    }

    public class DeviceConfig : AwtrixAddress
    {
        public List<AppConfig> Apps { get; set; } = new List<AppConfig>();
    }

    public class AwtrixAddress
    {
        /// <summary>
        /// eg: "awtrix/clock1" 
        /// </summary>
        public string BaseTopic { get; set; }

    }
}
