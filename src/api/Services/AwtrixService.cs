using AwtrixSharpWeb.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace AwtrixSharpWeb.Services
{
    public class AwtrixService : IAwtrixService
    {
        private readonly HttpPublisher _httpPublisher;
        private readonly MqttPublisher _mqttPublisher;
        private readonly ILogger _logger;

        public AwtrixService(HttpPublisher httpPublisher, MqttPublisher mqttPublisher, ILogger<AwtrixService>? logger = null)
        {
            _httpPublisher = httpPublisher;
            _mqttPublisher = mqttPublisher;
            _logger = (ILogger?)logger ?? NullLogger.Instance;
        }

        /// <summary>
        /// https://blueforcer.github.io/awtrix3/#/api?id=change-settings
        /// </summary>
        public Task<bool> Set(AwtrixAddress awtrixAddress, AwtrixSettings settings)
        {
            if (IsNull(awtrixAddress, nameof(awtrixAddress), nameof(Set)) || IsNull(settings, nameof(settings), nameof(Set)))
            {
                return Task.FromResult(false);
            }

            var baseTopic = awtrixAddress.BaseTopic;
            var payload = settings.ToJson();
            return SafePublish(baseTopic, p => p.Publish(baseTopic + "/settings", payload));
        }

        /// <summary>
        /// https://blueforcer.github.io/awtrix3/#/api?id=sound-playback
        /// </summary>
        public Task<bool> PlayRtttl(AwtrixAddress awtrixAddress, string rtttl)
        {
            if (IsNull(awtrixAddress, nameof(awtrixAddress), nameof(PlayRtttl)) || IsNull(rtttl, nameof(rtttl), nameof(PlayRtttl)))
            {
                return Task.FromResult(false);
            }

            var baseTopic = awtrixAddress.BaseTopic;
            return SafePublish(baseTopic, p => p.Publish(baseTopic + "/rtttl", rtttl));
        }

        public Task<bool> AppUpdate(AwtrixAddress awtrixAddress, string appName, AwtrixAppMessage message)
        {
            if (IsNull(awtrixAddress, nameof(awtrixAddress), nameof(AppUpdate)) || IsNull(message, nameof(message), nameof(AppUpdate)))
            {
                return Task.FromResult(false);
            }

            var baseTopic = awtrixAddress.BaseTopic;
            return SafePublish(baseTopic, p => p.Publish(p.BuildCustomAppUrl(baseTopic, appName), message));
        }

        public Task<bool> AppClear(AwtrixAddress awtrixAddress, string appName)
        {
            if (IsNull(awtrixAddress, nameof(awtrixAddress), nameof(AppClear)))
            {
                return Task.FromResult(false);
            }

            var baseTopic = awtrixAddress.BaseTopic;
            return SafePublish(baseTopic, p => p.Publish(p.BuildCustomAppUrl(baseTopic, appName), (AwtrixAppMessage?)null));
        }

        public Task<bool> Notify(AwtrixAddress awtrixAddress, AwtrixAppMessage message)
        {
            if (IsNull(awtrixAddress, nameof(awtrixAddress), nameof(Notify)) || IsNull(message, nameof(message), nameof(Notify)))
            {
                return Task.FromResult(false);
            }

            if (String.IsNullOrWhiteSpace(message.Text))
            {
                return Dismiss(awtrixAddress);
            }

            var baseTopic = awtrixAddress.BaseTopic;
            return SafePublish(baseTopic, p => p.Publish(baseTopic + "/notify", message));
        }

        /// <summary>
        /// Blink acts as a sign of life
        /// </summary>
        public static (int quantized, int quantizedBlink) Quantize(int progress)
        {
            int p = Math.Clamp(progress, 0, 100);

            int LedCount(int v)
            {
                if (v < 4) return 0;
                if (v == 100) return 32;
                return 1 + (int)Math.Floor((v - 4) * 31.0 / 96.0); // 4–99 => 1–31
            }

            int n = LedCount(p);
            int blink;

            if (n == 0) blink = 4;            // nothing lit
            else if (n == 32) blink = 99;     // drop to 31 LEDs
            else
            {
                int lowerBound = (n == 1) ? 4 : (int)Math.Ceiling(4 + 96.0 * (n - 1) / 31.0);
                blink = Math.Clamp(lowerBound - 1, 0, 99); // one bin lower
            }

            return (p, blink);
        }

        /// <remarks>https://blueforcer.github.io/awtrix3/#/api?id=dismiss-notification</remarks>
        public Task<bool> Dismiss(AwtrixAddress awtrixAddress)
        {
            if (IsNull(awtrixAddress, nameof(awtrixAddress), nameof(Dismiss)))
            {
                return Task.FromResult(false);
            }

            var baseTopic = awtrixAddress.BaseTopic;
            return SafePublish(baseTopic, p => p.Publish(baseTopic + "/notify/dismiss", (AwtrixAppMessage?)null));
        }

        /// <summary>
        /// WS1 deferred: a null argument is a caller bug, but publishing must never throw. Logs and reports "not delivered".
        /// </summary>
        private bool IsNull(object? argument, string argumentName, string operation)
        {
            if (argument != null)
            {
                return false;
            }

            _logger.LogWarning("{Operation} called with a null {Argument}; nothing published", operation, argumentName);
            return true;
        }

        /// <summary>
        /// Defensive wrapper: publishers must not throw, but if one does the failure is contained here.
        /// Publishers log the failure reason themselves, so a plain false is only logged at Debug.
        /// </summary>
        private async Task<bool> SafePublish(string baseTopic, Func<AwtrixPublisher, Task<bool>> publish)
        {
            try
            {
                var publisher = ResolvePublisher(baseTopic);
                var delivered = await publish(publisher);
                if (!delivered)
                {
                    _logger.LogDebug("Publish via {Publisher} for {BaseTopic} was not delivered", publisher.GetType().Name, baseTopic);
                }
                return delivered;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Publisher threw for {BaseTopic}; treating as not delivered", baseTopic);
                return false;
            }
        }

        private AwtrixPublisher ResolvePublisher(string baseTopic)
        {
            return AwtrixAddress.IsHttpTopic(baseTopic) ? _httpPublisher : _mqttPublisher;
        }
    }
}
