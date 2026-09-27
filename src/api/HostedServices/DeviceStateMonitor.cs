using System.Text;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using Microsoft.Extensions.Options;
using MQTTnet;

namespace AwtrixSharpWeb.HostedServices
{
    /// <summary>Latest state a device published. Capabilities fall back to the built-in lists until received.</summary>
    public sealed record DeviceState(DeviceCapabilities Capabilities, string? SettingsJson, string? DeviceJson, DateTimeOffset? LastSeen)
    {
        public static readonly DeviceState Empty = new(DeviceCapabilities.BuiltIn, null, null, null);
    }

    /// <summary>
    /// Caches each MQTT device's {baseTopic}/state/capabilities, /settings and /device payloads for the test UI.
    /// HTTP devices are not polled and always report the built-in capabilities. Never throws into the connector.
    /// </summary>
    public class DeviceStateMonitor : IHostedService
    {
        public const string CapabilitiesKind = "capabilities";
        public const string SettingsKind = "settings";
        public const string DeviceKind = "device";
        private static readonly string[] Kinds = { CapabilitiesKind, SettingsKind, DeviceKind };

        private readonly IMqttConnector _mqtt;
        private readonly AwtrixConfig _config;
        private readonly ILogger<DeviceStateMonitor> _logger;
        private readonly TimeProvider _time;
        private readonly object _lock = new();
        private readonly Dictionary<string, DeviceState> _states = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (string BaseTopic, string Kind)> _topics = new(StringComparer.Ordinal);

        public DeviceStateMonitor(IMqttConnector mqtt, IOptions<AwtrixConfig> config, ILogger<DeviceStateMonitor> logger, TimeProvider? time = null)
        {
            _mqtt = mqtt;
            _config = config.Value;
            _logger = logger;
            _time = time ?? TimeProvider.System;
        }

        /// <summary>Raised with the device base topic after any of its state payloads changes.</summary>
        public event Action<string>? Changed;

        public static string StateTopic(string baseTopic, string kind) => $"{baseTopic.TrimEnd('/')}/state/{kind}";

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var mqttDevices = _config.Devices.Where(d => !d.IsHttp && !string.IsNullOrWhiteSpace(d.BaseTopic)).ToList();
            foreach (var device in mqttDevices)
            {
                foreach (var kind in Kinds)
                {
                    _topics[StateTopic(device.BaseTopic, kind)] = (device.BaseTopic, kind);
                }
            }

            if (_topics.Count == 0)
            {
                return;
            }

            // Attach first: a retained message can arrive as soon as the subscription is acknowledged.
            _mqtt.MessageReceived += OnMessage;
            foreach (var topic in _topics.Keys)
            {
                await _mqtt.Subscribe(topic);
            }
            _logger.LogInformation("Watching state topics for {Count} MQTT device(s)", mqttDevices.Count);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _mqtt.MessageReceived -= OnMessage;
            return Task.CompletedTask;
        }

        public DeviceState Get(string baseTopic)
        {
            lock (_lock)
            {
                return _states.TryGetValue(baseTopic, out var state) ? state : DeviceState.Empty;
            }
        }

        private Task OnMessage(MqttApplicationMessageReceivedEventArgs args)
        {
            try
            {
                if (!_topics.TryGetValue(args.ApplicationMessage.Topic ?? string.Empty, out var target))
                {
                    return Task.CompletedTask;
                }

                var payload = Encoding.UTF8.GetString(args.ApplicationMessage.Payload);
                var now = _time.GetUtcNow();

                lock (_lock)
                {
                    var current = Get(target.BaseTopic);
                    var next = target.Kind switch
                    {
                        CapabilitiesKind => current with { Capabilities = ParseCapabilities(target.BaseTopic, payload, current.Capabilities), LastSeen = now },
                        SettingsKind => current with { SettingsJson = payload, LastSeen = now },
                        _ => current with { DeviceJson = payload, LastSeen = now },
                    };
                    _states[target.BaseTopic] = next;
                }

                RaiseChanged(target.BaseTopic);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process device state message on {Topic}", args.ApplicationMessage.Topic);
            }
            return Task.CompletedTask;
        }

        private DeviceCapabilities ParseCapabilities(string baseTopic, string payload, DeviceCapabilities previous)
        {
            if (DeviceCapabilities.TryParse(payload, out var parsed))
            {
                return parsed;
            }
            _logger.LogWarning("Ignoring malformed capabilities payload from {BaseTopic}; keeping previous", baseTopic);
            return previous;
        }

        private void RaiseChanged(string baseTopic)
        {
            try
            {
                Changed?.Invoke(baseTopic);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "A DeviceStateMonitor.Changed subscriber threw");
            }
        }
    }
}
