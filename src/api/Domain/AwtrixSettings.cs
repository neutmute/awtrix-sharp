namespace AwtrixSharpWeb.Domain
{
    public class AwtrixSettings : Dictionary<string, string>
    {
        public AwtrixSettings SetGlobalTextColor(string value)
        {
            this["TCOL"] = value;
            return this;
        }

        public AwtrixSettings SetBrightness(byte value)
        {
            this["BRI"] = value.ToString();
            return this;
        }

        public string ToJson()
        {
            var json = System.Text.Json.JsonSerializer.Serialize(this);
            return json;
        }

        public override string ToString()
        {
            return Keys
                .Select(k => $"{k}={this[k]}")
                .Aggregate((a, b) => $"{a};{b}");   
        }
    }
}