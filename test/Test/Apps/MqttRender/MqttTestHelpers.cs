using MQTTnet;
using MQTTnet.Packets;

namespace Test.Apps.MqttRender
{
    internal static class MqttTestHelpers
    {
        public static MqttApplicationMessageReceivedEventArgs CreateReceivedArgs(string topic, string payload)
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload ?? string.Empty)
                .Build();

            var packet = new MqttPublishPacket();

            return new MqttApplicationMessageReceivedEventArgs(
                "test-client",
                message,
                packet,
                (_, _) => Task.CompletedTask);
        }
    }
}
