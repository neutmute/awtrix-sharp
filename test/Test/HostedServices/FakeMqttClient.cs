using MQTTnet;
using MQTTnet.Diagnostics.PacketInspection;
using MQTTnet.Exceptions;
using Test.Apps.MqttRender;

namespace Test.HostedServices
{
    /// <summary>
    /// Hand-rolled IMqttClient for connector tests. Mirrors the MQTTnet 5 behaviours the connector
    /// depends on (verified against MQTTnet 5.0.1.1416):
    /// a failed ConnectAsync raises DisconnectedAsync(clientWasConnected: false) and then throws;
    /// Publish/Subscribe while disconnected throw MqttClientNotConnectedException;
    /// DisconnectAsync raises DisconnectedAsync(clientWasConnected: true);
    /// ConnectAsync after Dispose throws ObjectDisposedException.
    /// </summary>
    internal sealed class FakeMqttClient : IMqttClient
    {
        private readonly object _gate = new();

        /// <summary>Outcome of each ConnectAsync call in order (true = success). Empty queue = success.</summary>
        public Queue<bool> ConnectOutcomes { get; } = new();
        public int ConnectCallCount { get; private set; }
        public int DisconnectCallCount { get; private set; }
        public bool Disposed { get; private set; }
        public List<string> SubscribeRequests { get; } = new();
        public List<MqttApplicationMessage> Published { get; } = new();
        public Exception? PublishException { get; set; }
        public Exception? SubscribeException { get; set; }

        public bool IsConnected { get; private set; }
        public MqttClientOptions Options { get; private set; } = null!;

        public event Func<MqttApplicationMessageReceivedEventArgs, Task>? ApplicationMessageReceivedAsync;
        public event Func<MqttClientConnectedEventArgs, Task>? ConnectedAsync;
        public event Func<MqttClientConnectingEventArgs, Task>? ConnectingAsync;
        public event Func<MqttClientDisconnectedEventArgs, Task>? DisconnectedAsync;
        public event Func<InspectMqttPacketEventArgs, Task>? InspectPacketAsync;

        public int MessageReceivedSubscriberCount => ApplicationMessageReceivedAsync?.GetInvocationList().Length ?? 0;

        public async Task<MqttClientConnectResult> ConnectAsync(MqttClientOptions options, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            bool succeed;
            lock (_gate)
            {
                ConnectCallCount++;
                Options = options;
                succeed = ConnectOutcomes.Count == 0 || ConnectOutcomes.Dequeue();
            }

            if (!succeed)
            {
                var error = new MqttCommunicationException("Connection refused");
                await RaiseDisconnected(clientWasConnected: false, MqttClientDisconnectReason.UnspecifiedError, error);
                throw error;
            }

            IsConnected = true;
            var result = new MqttClientConnectResult();
            if (ConnectedAsync != null)
            {
                await ConnectedAsync(new MqttClientConnectedEventArgs(result));
            }
            return result;
        }

        public async Task DisconnectAsync(MqttClientDisconnectOptions options, CancellationToken cancellationToken)
        {
            DisconnectCallCount++;
            if (!IsConnected)
            {
                return;
            }
            IsConnected = false;
            await RaiseDisconnected(clientWasConnected: true, MqttClientDisconnectReason.NormalDisconnection, null);
        }

        /// <summary>Simulates the broker going away (restart, network loss, keep-alive timeout).</summary>
        public Task SimulateConnectionLostAsync()
        {
            IsConnected = false;
            return RaiseDisconnected(clientWasConnected: true, MqttClientDisconnectReason.ServerShuttingDown, null);
        }

        /// <summary>Simulates the broker delivering a message on a subscribed topic.</summary>
        public Task DeliverAsync(string topic, string payload)
        {
            var handler = ApplicationMessageReceivedAsync;
            return handler == null ? Task.CompletedTask : handler(MqttTestHelpers.CreateReceivedArgs(topic, payload));
        }

        public Task<MqttClientPublishResult> PublishAsync(MqttApplicationMessage applicationMessage, CancellationToken cancellationToken)
        {
            if (!IsConnected)
            {
                throw new MqttClientNotConnectedException();
            }
            if (PublishException != null)
            {
                throw PublishException;
            }
            lock (_gate)
            {
                Published.Add(applicationMessage);
            }
            return Task.FromResult(new MqttClientPublishResult(null, MqttClientPublishReasonCode.Success, null, Array.Empty<MQTTnet.Packets.MqttUserProperty>()));
        }

        public Task<MqttClientSubscribeResult> SubscribeAsync(MqttClientSubscribeOptions options, CancellationToken cancellationToken)
        {
            if (!IsConnected)
            {
                throw new MqttClientNotConnectedException();
            }
            if (SubscribeException != null)
            {
                throw SubscribeException;
            }
            lock (_gate)
            {
                SubscribeRequests.AddRange(options.TopicFilters.Select(f => f.Topic));
            }
            return Task.FromResult(new MqttClientSubscribeResult(0, Array.Empty<MqttClientSubscribeResultItem>(), null, Array.Empty<MQTTnet.Packets.MqttUserProperty>()));
        }

        public Task<MqttClientUnsubscribeResult> UnsubscribeAsync(MqttClientUnsubscribeOptions options, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task PingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendEnhancedAuthenticationExchangeDataAsync(MqttEnhancedAuthenticationExchangeData data, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Dispose()
        {
            Disposed = true;
            IsConnected = false;
        }

        private Task RaiseDisconnected(bool clientWasConnected, MqttClientDisconnectReason reason, Exception? exception)
        {
            var handler = DisconnectedAsync;
            return handler == null
                ? Task.CompletedTask
                : handler(new MqttClientDisconnectedEventArgs(clientWasConnected, null, reason, null, null, exception));
        }
    }
}
