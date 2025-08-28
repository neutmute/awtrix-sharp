using AwtrixSharpWeb.Apps.Configs;

namespace AwtrixSharpWeb.Domain
{
    public class DeviceConfig : AwtrixAddress
    {
        public List<AppConfig> Apps { get; set; } = new List<AppConfig>();
    }
}
