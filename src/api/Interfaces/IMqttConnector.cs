using MQTTnet;

namespace AwtrixSharpWeb.Interfaces
{
    public interface IMqttConnector
    {
        event Func<MqttApplicationMessageReceivedEventArgs, Task> MessageReceived;

        Task Subscribe(string topic);

        /// <summary>
        /// Returns true when the client accepted the publish; false (never throws) otherwise.
        /// </summary>
        Task<bool> PublishAsync(string topic, string payload);
    }
}
