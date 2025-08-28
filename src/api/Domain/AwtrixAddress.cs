namespace AwtrixSharpWeb.Domain
{

    public class AwtrixAddress
    {
        /// <summary>
        /// eg: "awtrix/clock1" 
        /// </summary>
        public string BaseTopic { get; set; }

        public override string ToString() => BaseTopic;
    }
}
