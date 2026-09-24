using System.Text.Json;
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Services.Firmware
{
    /// <summary>
    /// AWTRIX NG dialect. MQTT: {prefix}/cmd/... (https://blueforcer.github.io/awtrix-ng/reference/mqtt/).
    /// HTTP: {root}/api/v1/... with real verbs (https://blueforcer.github.io/awtrix-ng/reference/http/).
    /// BaseTopic is the device's mqttPrefix or its root URL; a trailing /api or /api/v1 is tolerated.
    /// </summary>
    public sealed class NgFirmware : IAwtrixFirmware
    {
        public AwtrixFirmwareKind Kind => AwtrixFirmwareKind.NG;

        public AwtrixRequest AppUpdate(AwtrixAddress address, string appName, AwtrixAppMessage message)
        {
            var t = NgPayloadTranslator.Translate(message, NgPayloadKind.App);
            return address.IsHttp
                ? new AwtrixRequest($"{HttpRoot(address.BaseTopic)}/api/v1/apps/pushed/{Uri.EscapeDataString(appName)}", HttpMethod.Put, t.Json, t.DroppedKeys)
                : new AwtrixRequest($"{address.BaseTopic}/cmd/apps/pushed/{appName}", HttpMethod.Post, t.Json, t.DroppedKeys);
        }

        public AwtrixRequest AppClear(AwtrixAddress address, string appName)
        {
            // MQTT keeps delete-by-empty-payload; HTTP needs an explicit DELETE (an empty PUT is a 422)
            return address.IsHttp
                ? new AwtrixRequest($"{HttpRoot(address.BaseTopic)}/api/v1/apps/{Uri.EscapeDataString(appName)}", HttpMethod.Delete, string.Empty)
                : AwtrixRequest.Post($"{address.BaseTopic}/cmd/apps/pushed/{appName}", string.Empty);
        }

        public AwtrixRequest Notify(AwtrixAddress address, AwtrixAppMessage message)
        {
            var t = NgPayloadTranslator.Translate(message, NgPayloadKind.Notification);
            return address.IsHttp
                ? new AwtrixRequest($"{HttpRoot(address.BaseTopic)}/api/v1/notifications", HttpMethod.Post, t.Json, t.DroppedKeys)
                : new AwtrixRequest($"{address.BaseTopic}/cmd/notify", HttpMethod.Post, t.Json, t.DroppedKeys);
        }

        public AwtrixRequest Dismiss(AwtrixAddress address)
        {
            return address.IsHttp
                ? new AwtrixRequest($"{HttpRoot(address.BaseTopic)}/api/v1/notifications/active", HttpMethod.Delete, string.Empty)
                : AwtrixRequest.Post($"{address.BaseTopic}/cmd/notify/dismiss", string.Empty);
        }

        public AwtrixRequest Settings(AwtrixAddress address, AwtrixSettings settings)
        {
            var t = NgSettingsTranslator.Translate(settings);
            return address.IsHttp
                ? new AwtrixRequest($"{HttpRoot(address.BaseTopic)}/api/v1/settings", HttpMethod.Patch, t.Json, t.DroppedKeys)
                : new AwtrixRequest($"{address.BaseTopic}/cmd/settings", HttpMethod.Post, t.Json, t.DroppedKeys);
        }

        public AwtrixRequest PlayRtttl(AwtrixAddress address, string rtttl)
        {
            var payload = JsonSerializer.Serialize(new Dictionary<string, string> { ["rtttl"] = rtttl });
            return address.IsHttp
                ? new AwtrixRequest($"{HttpRoot(address.BaseTopic)}/api/v1/audio/play", HttpMethod.Post, payload)
                : AwtrixRequest.Post($"{address.BaseTopic}/cmd/audio/play", payload);
        }

        public string ButtonTopic(AwtrixAddress address, Button button)
            => $"{address.BaseTopic}/state/buttons/{button.ToString().ToLowerInvariant()}";

        /// <summary>Device root without a trailing slash, /api or /api/v1 (copied-from-AWTRIX-3 addresses).</summary>
        public static string HttpRoot(string baseTopic)
        {
            var root = baseTopic.TrimEnd('/');
            foreach (var suffix in new[] { "/api/v1", "/api" })
            {
                if (root.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    root = root.Substring(0, root.Length - suffix.Length).TrimEnd('/');
                    break;
                }
            }
            return root;
        }
    }
}
