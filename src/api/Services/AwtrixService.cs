using AwtrixSharpWeb.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using System.Linq;
using System.Text.Json;

namespace AwtrixSharpWeb.Services
{
    /// <summary>
    /// Sends operations to an AWTRIX NG device (https://blueforcer.github.io/awtrix-ng/reference/payload/).
    /// Never throws: every failure is logged and reported as false.
    /// </summary>
    public class AwtrixService : IAwtrixService
    {
        /// <summary>NG rejects a pushed-app payload carrying these (notification-only) keys with 422.</summary>
        internal static readonly string[] NotificationOnlyKeys = { "hold", "stack" };

        /// <summary>NG rejects a notification payload carrying these (pushed-app-only) keys with 422.</summary>
        internal static readonly string[] AppOnlyKeys = { "lifetimeMs", "lifetimeExpiry" };

        private readonly HttpPublisher _httpPublisher;
        private readonly MqttPublisher _mqttPublisher;
        private readonly ILogger _logger;
        private readonly PublishTrace? _trace;
        private readonly TimeProvider _timeProvider;

        public AwtrixService(HttpPublisher httpPublisher, MqttPublisher mqttPublisher, ILogger<AwtrixService>? logger = null, PublishTrace? trace = null, TimeProvider? timeProvider = null)
        {
            _httpPublisher = httpPublisher;
            _mqttPublisher = mqttPublisher;
            _logger = (ILogger?)logger ?? NullLogger.Instance;
            _trace = trace;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public Task<bool> Set(AwtrixAddress awtrixAddress, AwtrixSettings settings)
        {
            if (IsNull(awtrixAddress, nameof(awtrixAddress), nameof(Set)) || IsNull(settings, nameof(settings), nameof(Set)))
            {
                return Task.FromResult(false);
            }

            return SafePublish(awtrixAddress, "Settings", null, () => AwtrixEndpoints.Settings(awtrixAddress, settings.ToJson()));
        }

        public Task<bool> PlayRtttl(AwtrixAddress awtrixAddress, string rtttl)
        {
            if (IsNull(awtrixAddress, nameof(awtrixAddress), nameof(PlayRtttl)) || IsNull(rtttl, nameof(rtttl), nameof(PlayRtttl)))
            {
                return Task.FromResult(false);
            }

            return SafePublish(awtrixAddress, "Rtttl", null, () => AwtrixEndpoints.PlayRtttl(awtrixAddress, rtttl));
        }

        public Task<bool> AppUpdate(AwtrixAddress awtrixAddress, string appName, AwtrixAppMessage message)
        {
            if (IsNull(awtrixAddress, nameof(awtrixAddress), nameof(AppUpdate)) || IsNull(message, nameof(message), nameof(AppUpdate)))
            {
                return Task.FromResult(false);
            }

            return SafePublish(awtrixAddress, "AppUpdate", appName, () => AwtrixEndpoints.AppUpdate(awtrixAddress, appName, Serialize(message, NotificationOnlyKeys, awtrixAddress)));
        }

        public Task<bool> AppClear(AwtrixAddress awtrixAddress, string appName)
        {
            if (IsNull(awtrixAddress, nameof(awtrixAddress), nameof(AppClear)))
            {
                return Task.FromResult(false);
            }

            return SafePublish(awtrixAddress, "AppClear", appName, () => AwtrixEndpoints.AppClear(awtrixAddress, appName));
        }

        public Task<bool> Notify(AwtrixAddress awtrixAddress, AwtrixAppMessage message)
        {
            if (IsNull(awtrixAddress, nameof(awtrixAddress), nameof(Notify)) || IsNull(message, nameof(message), nameof(Notify)))
            {
                return Task.FromResult(false);
            }

            if (IsBlankText(message))
            {
                return Dismiss(awtrixAddress);
            }

            return SafePublish(awtrixAddress, "Notify", null, () => AwtrixEndpoints.Notify(awtrixAddress, Serialize(message, AppOnlyKeys, awtrixAddress)));
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

        public Task<bool> Dismiss(AwtrixAddress awtrixAddress)
        {
            if (IsNull(awtrixAddress, nameof(awtrixAddress), nameof(Dismiss)))
            {
                return Task.FromResult(false);
            }

            return SafePublish(awtrixAddress, "Dismiss", null, () => AwtrixEndpoints.Dismiss(awtrixAddress));
        }

        /// <summary>No "text" key, a null/whitespace string, or a fragment array that is empty or whose fragments are all whitespace.</summary>
        private static bool IsBlankText(AwtrixAppMessage message)
        {
            if (!message.TryGetValue("text", out var value))
            {
                return true;
            }

            return value switch
            {
                string s => string.IsNullOrWhiteSpace(s),
                JsonElement { ValueKind: JsonValueKind.String } e => string.IsNullOrWhiteSpace(e.GetString()),
                TextFragment[] fragments => fragments.Length == 0 || fragments.All(f => string.IsNullOrWhiteSpace(f.Text)),
                _ => false,
            };
        }

        /// <summary>Serializes the message without keys the target operation cannot carry; each removal is logged at Debug.</summary>
        private string Serialize(AwtrixAppMessage message, string[] excludedKeys, AwtrixAddress address)
        {
            foreach (var key in excludedKeys)
            {
                if (message.ContainsKey(key))
                {
                    _logger.LogDebug("'{Key}' is not valid for this operation; omitted for {BaseTopic}", key, address.BaseTopic);
                }
            }
            return message.ToJson(excludedKeys);
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
        /// Builds the request and hands it to the transport for its address. Publishers must not throw,
        /// but if one (or the request builder) does the failure is contained here.
        /// </summary>
        private async Task<bool> SafePublish(AwtrixAddress address, string operation, string? appName, Func<AwtrixRequest> build)
        {
            var baseTopic = address.BaseTopic;
            AwtrixRequest? request = null;
            var delivered = false;
            try
            {
                request = build();
                var publisher = ResolvePublisher(baseTopic);
                _logger.LogDebug("{Publisher} {Method} {Address} payload: {Payload}", publisher.GetType().Name, request.Method, request.Address, request.Payload);
                delivered = await publisher.Publish(request);
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
            finally
            {
                if (_trace != null && request != null)
                {
                    _trace.Record(new PublishRecord(_timeProvider.GetUtcNow(), baseTopic, operation, appName, request, delivered));
                }
            }
        }

        private AwtrixPublisher ResolvePublisher(string baseTopic)
        {
            return AwtrixAddress.IsHttpTopic(baseTopic) ? _httpPublisher : _mqttPublisher;
        }
    }
}
