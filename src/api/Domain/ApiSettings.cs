namespace AwtrixSharpWeb.Domain
{
    /// <summary>
    /// Optional HTTP API settings (section "Api", env AWTRIXSHARP_API__KEY). An unset Key means no check,
    /// which is the historical behaviour. Making the key mandatory is a deferred owner decision.
    /// </summary>
    public class ApiSettings
    {
        public const string SectionName = "Api";
        public const string KeyConfigurationKey = "Api:Key";

        public string? Key { get; set; }
    }
}
