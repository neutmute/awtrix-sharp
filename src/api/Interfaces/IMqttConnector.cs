using MQTTnet;

namespace AwtrixSharpWeb.Interfaces
{
    public interface IMqttConnector
    {
        /// <summary>
        /// Raised for every message received on any subscribed topic. Handlers are owned by the
        /// connector (not the underlying client), so they survive reconnects. A throwing handler
        /// is logged and does not prevent other handlers from running.
        /// </summary>
        event Func<MqttApplicationMessageReceivedEventArgs, Task> MessageReceived;

        /// <summary>
        /// Records the topic in the subscription registry and subscribes immediately when connected.
        /// While disconnected the subscription is deferred and applied on the next connect.
        /// Never throws.
        /// </summary>
        Task Subscribe(string topic);

        /// <summary>
        /// Returns true when the client accepted the publish; false (never throws) otherwise,
        /// including while disconnected. Failed publishes are not queued.
        /// </summary>
        Task<bool> PublishAsync(string topic, string payload);
    }
}
