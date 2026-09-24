using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Services.Firmware
{
    /// <summary>
    /// One firmware dialect: where each operation goes (topic or URL + HTTP method) and what its body is.
    /// Implementations are pure and stateless; obtain them from <see cref="AwtrixFirmware.For"/>.
    /// </summary>
    public interface IAwtrixFirmware
    {
        AwtrixFirmwareKind Kind { get; }
        AwtrixRequest AppUpdate(AwtrixAddress address, string appName, AwtrixAppMessage message);
        AwtrixRequest AppClear(AwtrixAddress address, string appName);
        AwtrixRequest Notify(AwtrixAddress address, AwtrixAppMessage message);
        AwtrixRequest Dismiss(AwtrixAddress address);
        AwtrixRequest Settings(AwtrixAddress address, AwtrixSettings settings);
        AwtrixRequest PlayRtttl(AwtrixAddress address, string rtttl);
        /// <summary>MQTT topic the device publishes 0/1 button state on. Only meaningful for MQTT addresses.</summary>
        string ButtonTopic(AwtrixAddress address, Button button);
    }
}
