namespace AwtrixSharpWeb.Services.Firmware
{
    /// <summary>
    /// A fully addressed message for a device. Address is an MQTT topic or an absolute URL; Method only
    /// matters over HTTP. Payload is string.Empty for bodiless requests. DroppedKeys lists message keys a
    /// firmware profile could not express (so the caller can warn); empty for AWTRIX 3.
    /// </summary>
    public sealed record AwtrixRequest(string Address, HttpMethod Method, string Payload, IReadOnlyList<string>? DroppedKeys = null)
    {
        public IReadOnlyList<string> DroppedKeys { get; init; } = DroppedKeys ?? Array.Empty<string>();

        public static AwtrixRequest Post(string address, string payload) => new(address, HttpMethod.Post, payload);
    }
}
