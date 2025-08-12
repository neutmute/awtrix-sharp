namespace AwtrixSharpWeb.Domain
{
    public class AwtrixConfig
    {
        public AwtrixDevice[] Devices { get; set; } = Array.Empty<AwtrixDevice>();
    }

    public class AwtrixDevice
    {
        /// <summary>
        /// eg: "awtrix/clock1" 
        /// </summary>
        public string BaseTopic { get; set; }

    }
}
