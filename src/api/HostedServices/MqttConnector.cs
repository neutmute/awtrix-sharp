using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Formatter;
using System.Text;

namespace AwtrixSharpWeb.HostedServices
{
    /// <summary>
    /// Owns the single, long-lived MQTT client for the process.
    /// <list type="bullet">
    /// <item>The client is created once and reconnected in place; it is never replaced.</item>
    /// <item>Message handlers and topic subscriptions are held here, so they survive reconnects.</item>
    /// <item>No public member throws for broker/network faults.</item>
    /// </list>
    /// </summary>
    public class MqttConnector : IHostedService, IMqttConnector, IDisposable
    {
        internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

        private readonly IMqttClient _client;
        private readonly MqttClientOptions _clientOptions;
        private readonly ILogger<MqttConnector> _log;
        private readonly MqttSettings _settings;
        private readonly SemaphoreSlim _connectLock = new(1, 1);
        private readonly object _registryLock = new();
        private readonly HashSet<string> _topics = new(StringComparer.Ordinal);
        private Func<MqttApplicationMessageReceivedEventArgs, Task>? _messageHandlers;
        private int _disposed;

        public event Func<MqttApplicationMessageReceivedEventArgs, Task> MessageReceived
        {
            add { lock (_registryLock) { _messageHandlers += value; } }
            remove { lock (_registryLock) { _messageHandlers -= value; } }
        }

        public MqttConnector(ILogger<MqttConnector> logger, IOptions<MqttSettings> settings)
            : this(logger, settings, new MqttClientFactory().CreateMqttClient())
        {
        }

        /// <summary>
        /// Test seam: inject the client so connector behaviour runs without a broker.
        /// </summary>
        internal MqttConnector(ILogger<MqttConnector> logger, IOptions<MqttSettings> settings, IMqttClient client)
        {
            _log = logger;
            _settings = settings.Value;
            _client = client;
            _clientOptions = BuildClientOptions(_settings);

            _client.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
        }

        private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        internal static MqttClientOptions BuildClientOptions(MqttSettings settings)
        {
            var builder = new MqttClientOptionsBuilder()
                .WithTcpServer(settings.Host)
                .WithProtocolVersion(MqttProtocolVersion.V500);

            if (!string.IsNullOrEmpty(settings.Username))
            {
                builder.WithCredentials(settings.Username, settings.Password);
            }

            return builder.Build();
        }

        /// <summary>
        /// Connects the one client if it is not already connected, then replays the subscription registry.
        /// Returns false (never throws) on failure, timeout, cancellation or after disposal.
        /// </summary>
        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (IsDisposed)
            {
                return false;
            }

            try
            {
                await _connectLock.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            try
            {
                if (_client.IsConnected)
                {
                    return true;
                }

                _log.LogDebug("Connecting to MQTT broker at {Host}...", _settings.Host);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(ConnectTimeout);

                await _client.ConnectAsync(_clientOptions, timeoutCts.Token);

                if (!_client.IsConnected)
                {
                    _log.LogWarning("MQTT broker at {Host} did not accept the connection", _settings.Host);
                    return false;
                }

                _log.LogInformation("Connected to MQTT broker at {Host}", _settings.Host);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _log.LogWarning("Connection to MQTT broker at {Host} timed out after {Timeout}", _settings.Host, ConnectTimeout);
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Failed to connect to MQTT broker at {Host}: {Error}", _settings.Host, ex.Message);
                return false;
            }
            finally
            {
                _connectLock.Release();
            }

            await ReplaySubscriptionsAsync();
            return _client.IsConnected;
        }

        public async Task Subscribe(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic))
            {
                _log.LogWarning("Ignoring MQTT subscribe with an empty topic");
                return;
            }

            lock (_registryLock)
            {
                _topics.Add(topic);
            }

            if (IsDisposed || !_client.IsConnected)
            {
                _log.LogDebug("MQTT not connected; subscription to {Topic} will be applied on connect", topic);
                return;
            }

            // Always send SUBSCRIBE, even for a known topic: the broker re-sends retained
            // messages, which MqttRenderApp relies on at each activation.
            await SendSubscribeAsync(topic);
        }

        public async Task<bool> PublishAsync(string topic, string payload)
        {
            payload ??= string.Empty;

            if (IsDisposed || !_client.IsConnected)
            {
                _log.LogDebug("MQTT not connected; dropping publish to {Topic}", topic);
                return false;
            }

            _log.LogDebug("Publishing MQTT to topic {Topic} with payload {Payload}", topic, payload.Length == 0 ? "<empty>" : payload);

            try
            {
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(Encoding.UTF8.GetBytes(payload))
                    .Build();

                var result = await _client.PublishAsync(message, CancellationToken.None);

                if (result is { IsSuccess: false })
                {
                    _log.LogWarning("MQTT publish to {Topic} was rejected: {ReasonCode}", topic, result.ReasonCode);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                // No reconnect here: reconnecting is owned by the DisconnectedAsync handler (Task 2).
                _log.LogWarning("MQTT publish to {Topic} failed: {Error}", topic, ex.Message);
                return false;
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (!await ConnectAsync(cancellationToken))
            {
                _log.LogWarning("MQTT broker at {Host} unavailable at startup", _settings.Host);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (IsDisposed)
            {
                return;
            }

            if (_client.IsConnected)
            {
                try
                {
                    await _client.DisconnectAsync(new MqttClientDisconnectOptions(), cancellationToken);
                }
                catch (Exception ex)
                {
                    _log.LogWarning("MQTT disconnect during shutdown failed: {Error}", ex.Message);
                }
            }

            Dispose();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _client.ApplicationMessageReceivedAsync -= OnApplicationMessageReceivedAsync;
            _client.Dispose();
        }

        private async Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
        {
            Func<MqttApplicationMessageReceivedEventArgs, Task>? handlers;
            lock (_registryLock)
            {
                handlers = _messageHandlers;
            }

            if (handlers == null)
            {
                return;
            }

            foreach (var handler in handlers.GetInvocationList().Cast<Func<MqttApplicationMessageReceivedEventArgs, Task>>())
            {
                try
                {
                    await handler(args);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "MQTT message handler {Handler} failed for topic {Topic}",
                        $"{handler.Method.DeclaringType?.Name}.{handler.Method.Name}", args.ApplicationMessage?.Topic);
                }
            }
        }

        private async Task ReplaySubscriptionsAsync()
        {
            string[] topics;
            lock (_registryLock)
            {
                topics = _topics.ToArray();
            }

            foreach (var topic in topics)
            {
                await SendSubscribeAsync(topic);
            }

            if (topics.Length > 0)
            {
                _log.LogInformation("Subscribed to {Count} MQTT topic(s) after connect", topics.Length);
            }
        }

        private async Task SendSubscribeAsync(string topic)
        {
            try
            {
                var options = new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(topic)
                    .Build();

                await _client.SubscribeAsync(options, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _log.LogWarning("MQTT subscribe to {Topic} failed; it will be retried on the next connect: {Error}", topic, ex.Message);
            }
        }
    }
}
