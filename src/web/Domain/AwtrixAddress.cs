namespace AwtrixSharpWeb.Domain
{
    public class AwtrixConfig
    {
        public AwtrixAddress[] Devices { get; set; } = Array.Empty<AwtrixAddress>();
    }

    public class AwtrixAddress
    {
        /// <summary>
        /// eg: "awtrix/clock1" 
        /// </summary>
        public string BaseTopic { get; set; }

    }
}
