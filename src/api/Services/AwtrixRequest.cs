namespace AwtrixSharpWeb.Services
{
    /// <summary>
    /// A fully addressed message for a device. Address is an MQTT topic or an absolute URL; Method only
    /// matters over HTTP. Payload is string.Empty for bodiless requests.
    /// </summary>
    public sealed record AwtrixRequest(string Address, HttpMethod Method, string Payload)
    {
        public static AwtrixRequest Post(string address, string payload) => new(address, HttpMethod.Post, payload);
    }
}
