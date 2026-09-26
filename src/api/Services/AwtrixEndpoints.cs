using System.Text.Json;
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Services
{
    /// <summary>
    /// Where each operation goes on an AWTRIX NG device. MQTT: {prefix}/cmd/... (reference/mqtt/).
    /// HTTP: {root}/api/v1/... with real verbs (reference/http/). BaseTopic is the device's mqttPrefix or its
    /// root URL such as http://192.168.1.51 (no /api suffix; see docs/config-migration.md).
    /// </summary>
    public static class AwtrixEndpoints
    {
        public static AwtrixRequest AppUpdate(AwtrixAddress address, string appName, string json)
        {
            return address.IsHttp
                ? new AwtrixRequest($"{HttpRoot(address.BaseTopic)}/api/v1/apps/pushed/{Uri.EscapeDataString(appName)}", HttpMethod.Put, json)
                : AwtrixRequest.Post($"{address.BaseTopic}/cmd/apps/pushed/{appName}", json);
        }

        public static AwtrixRequest AppClear(AwtrixAddress address, string appName)
        {
            // MQTT deletes on an empty payload; HTTP needs an explicit DELETE (an empty PUT is a 422)
            return address.IsHttp
                ? new AwtrixRequest($"{HttpRoot(address.BaseTopic)}/api/v1/apps/{Uri.EscapeDataString(appName)}", HttpMethod.Delete, string.Empty)
                : AwtrixRequest.Post($"{address.BaseTopic}/cmd/apps/pushed/{appName}", string.Empty);
        }

        public static AwtrixRequest Notify(AwtrixAddress address, string json)
        {
            return address.IsHttp
                ? new AwtrixRequest($"{HttpRoot(address.BaseTopic)}/api/v1/notifications", HttpMethod.Post, json)
                : AwtrixRequest.Post($"{address.BaseTopic}/cmd/notify", json);
        }

        public static AwtrixRequest Dismiss(AwtrixAddress address)
        {
            return address.IsHttp
                ? new AwtrixRequest($"{HttpRoot(address.BaseTopic)}/api/v1/notifications/active", HttpMethod.Delete, string.Empty)
                : AwtrixRequest.Post($"{address.BaseTopic}/cmd/notify/dismiss", string.Empty);
        }

        public static AwtrixRequest Settings(AwtrixAddress address, string json)
        {
            return address.IsHttp
                ? new AwtrixRequest($"{HttpRoot(address.BaseTopic)}/api/v1/settings", HttpMethod.Patch, json)
                : AwtrixRequest.Post($"{address.BaseTopic}/cmd/settings", json);
        }

        public static AwtrixRequest PlayRtttl(AwtrixAddress address, string rtttl)
        {
            var payload = JsonSerializer.Serialize(new Dictionary<string, string> { ["rtttl"] = rtttl });
            return address.IsHttp
                ? new AwtrixRequest($"{HttpRoot(address.BaseTopic)}/api/v1/audio/play", HttpMethod.Post, payload)
                : AwtrixRequest.Post($"{address.BaseTopic}/cmd/audio/play", payload);
        }

        /// <summary>MQTT topic the device publishes 0/1 button state on.</summary>
        public static string ButtonTopic(AwtrixAddress address, Button button)
            => $"{address.BaseTopic}/state/buttons/{button.ToString().ToLowerInvariant()}";

        /// <summary>Device root without a trailing slash. No other normalisation: a stale "/api" suffix is sent as-is.</summary>
        public static string HttpRoot(string baseTopic) => baseTopic.TrimEnd('/');
    }
}
