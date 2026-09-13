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
    /// <item>A lost connection is retried in the background with exponential backoff.</item>
    /// <item>No public member throws for broker/network faults.</item>
    /// </list>
    /// </summary>
    public class MqttConnector : IHostedService, IMqttConnector, IDisposable
    {
        internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
        internal static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(60);

        private readonly IMqttClient _client;
        private readonly MqttClientOptions _clientOptions;
        private readonly ILogger<MqttConnector> _log;
        private readonly MqttSettings _settings;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly SemaphoreSlim _connectLock = new(1, 1);
        private readonly object _registryLock = new();
        private readonly HashSet<string> _topics = new(StringComparer.Ordinal);
        private readonly CancellationTokenSource _stopping = new();
        private Func<MqttApplicationMessageReceivedEventArgs, Task>? _messageHandlers;
        private Task _reconnectTask = Task.CompletedTask;
        private int _reconnectLoopRunning;
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
        /// Test seam: inject the client and the backoff delay so reconnect behaviour runs without a broker or real time.
        /// </summary>
        internal MqttConnector(
            ILogger<MqttConnector> logger,
            IOptions<MqttSettings> settings,
            IMqttClient client,
            Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _log = logger;
            _settings = settings.Value;
            _client = client;
            _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
            _clientOptions = BuildClientOptions(_settings);

            _client.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
            _client.DisconnectedAsync += OnDisconnectedAsync;
        }

        /// <summary>The current background reconnect loop (completed when none is running). For tests and shutdown.</summary>
        internal Task ReconnectTask => Volatile.Read(ref _reconnectTask);

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

        /// <summary>1 s, 2 s, 4 s ... doubling per attempt, capped at <see cref="MaxReconnectDelay"/>.</summary>
        internal static TimeSpan GetReconnectDelay(int attempt)
        {
            var seconds = Math.Pow(2, Math.Clamp(attempt, 0, 6));
            return TimeSpan.FromSeconds(Math.Min(seconds, MaxReconnectDelay.TotalSeconds));
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
                // No reconnect here: a dropped connection raises DisconnectedAsync, which owns reconnecting.
                _log.LogWarning("MQTT publish to {Topic} failed: {Error}", topic, ex.Message);
                return false;
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (!await ConnectAsync(cancellationToken))
            {
                _log.LogWarning("MQTT broker at {Host} unavailable at startup; retrying in the background", _settings.Host);
                EnsureReconnectLoop();
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (IsDisposed)
            {
                return;
            }

            _stopping.Cancel();

            try
            {
                await ReconnectTask.WaitAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
                _log.LogDebug("Stopped waiting for the MQTT reconnect loop");
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

            _stopping.Cancel();
            _client.ApplicationMessageReceivedAsync -= OnApplicationMessageReceivedAsync;
            _client.DisconnectedAsync -= OnDisconnectedAsync;
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

        private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
        {
            if (_stopping.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            if (args.ClientWasConnected)
            {
                _log.LogWarning("Disconnected from MQTT broker at {Host} ({Reason}); reconnecting in the background",
                    _settings.Host, args.Reason);
            }

            // Never await reconnect work inside MQTTnet's event pipeline.
            EnsureReconnectLoop();
            return Task.CompletedTask;
        }

        private void EnsureReconnectLoop()
        {
            if (_stopping.IsCancellationRequested)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _reconnectLoopRunning, 1, 0) != 0)
            {
                return;
            }

            Volatile.Write(ref _reconnectTask, Task.Run(RunReconnectLoopAsync));
        }

        private async Task RunReconnectLoopAsync()
        {
            var token = _stopping.Token;
            var faulted = false;

            try
            {
                for (var attempt = 0; !token.IsCancellationRequested && !_client.IsConnected; attempt++)
                {
                    await _delay(GetReconnectDelay(attempt), token);

                    if (await ConnectAsync(token))
                    {
                        _log.LogInformation("Reconnected to MQTT broker at {Host} after {Attempts} attempt(s)", _settings.Host, attempt + 1);
                        break;
                    }

                    _log.LogWarning("MQTT reconnect attempt {Attempt} to {Host} failed; next attempt in {Delay}",
                        attempt + 1, _settings.Host, GetReconnectDelay(attempt + 1));
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown.
            }
            catch (Exception ex)
            {
                faulted = true;
                _log.LogError(ex, "MQTT reconnect loop stopped unexpectedly");
            }
            finally
            {
                Interlocked.Exchange(ref _reconnectLoopRunning, 0);
            }

            // A disconnect that raced the end of the loop must not be lost.
            if (!faulted && !token.IsCancellationRequested && !IsDisposed && !_client.IsConnected)
            {
                EnsureReconnectLoop();
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
