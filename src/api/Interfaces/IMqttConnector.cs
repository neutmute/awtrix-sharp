using MQTTnet;

namespace AwtrixSharpWeb.Interfaces
{
    public interface IMqttConnector
    {
        event Func<MqttApplicationMessageReceivedEventArgs, Task> MessageReceived;

        Task Subscribe(string topic);
    }
}