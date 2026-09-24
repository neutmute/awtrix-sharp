using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Services.Firmware
{
    /// <summary>
    /// AWTRIX 3 dialect (https://blueforcer.github.io/awtrix3/#/api). Transitional: delete this class and the
    /// enum member when no AWTRIX 3 devices remain. Everything is a POST; clears are empty bodies.
    /// </summary>
    public sealed class Awtrix3Firmware : IAwtrixFirmware
    {
        public AwtrixFirmwareKind Kind => AwtrixFirmwareKind.Awtrix3;

        public AwtrixRequest AppUpdate(AwtrixAddress address, string appName, AwtrixAppMessage message)
            => AwtrixRequest.Post(CustomAppAddress(address, appName), message.ToJson());

        public AwtrixRequest AppClear(AwtrixAddress address, string appName)
            => AwtrixRequest.Post(CustomAppAddress(address, appName), string.Empty);

        public AwtrixRequest Notify(AwtrixAddress address, AwtrixAppMessage message)
            => AwtrixRequest.Post(address.BaseTopic + "/notify", message.ToJson());

        public AwtrixRequest Dismiss(AwtrixAddress address)
            => AwtrixRequest.Post(address.BaseTopic + "/notify/dismiss", string.Empty);

        public AwtrixRequest Settings(AwtrixAddress address, AwtrixSettings settings)
            => AwtrixRequest.Post(address.BaseTopic + "/settings", settings.ToJson());

        public AwtrixRequest PlayRtttl(AwtrixAddress address, string rtttl)
            => AwtrixRequest.Post(address.BaseTopic + "/rtttl", rtttl);

        public string ButtonTopic(AwtrixAddress address, Button button)
            => $"{address.BaseTopic}/stats/button{button}";

        /// <summary>MQTT: {base}/custom/{app}. HTTP: POST http://[ip]/api/custom?name=[app].</summary>
        private static string CustomAppAddress(AwtrixAddress address, string appName)
        {
            return address.IsHttp
                ? $"{address.BaseTopic.TrimEnd('/')}/custom?name={Uri.EscapeDataString(appName)}"
                : $"{address.BaseTopic}/custom/{appName}";
        }
    }
}
