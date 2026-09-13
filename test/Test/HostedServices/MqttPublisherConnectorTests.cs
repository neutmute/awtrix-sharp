using System.Buffers;
using System.Text;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Test.HostedServices
{
    /// <summary>
    /// MqttPublisher -> MqttConnector.PublishAsync end to end over a FakeMqttClient
    /// (injected through the connector's internal constructor; no reflection, no broker).
    /// </summary>
    public class MqttPublisherConnectorTests
    {
        private static async Task<(MqttConnector connector, FakeMqttClient client)> CreateConnectedAsync()
        {
            var client = new FakeMqttClient();
            var connector = new MqttConnector(NullLogger<MqttConnector>.Instance, Options.Create(new MqttSettings()), client);
            await connector.StartAsync(CancellationToken.None);
            return (connector, client);
        }

        [Fact]
        public async Task Publish_SendsCorrectTopicAndPayloadToMqttClient()
        {
            var (connector, client) = await CreateConnectedAsync();
            var publisher = new MqttPublisher(connector, NullLogger<MqttPublisher>.Instance);

            var result = await publisher.Publish("awtrix/clock1/notify", "{\"text\":\"hi\"}");

            Assert.True(result);
            var message = Assert.Single(client.Published);
            Assert.Equal("awtrix/clock1/notify", message.Topic);
            Assert.Equal("{\"text\":\"hi\"}", Encoding.UTF8.GetString(message.Payload.ToArray()));
        }

        [Fact]
        public async Task Publish_WithEmptyPayload_SendsEmptyByteArray()
        {
            var (connector, client) = await CreateConnectedAsync();

            await connector.PublishAsync("awtrix/clock1/notify/dismiss", string.Empty);

            Assert.Empty(Assert.Single(client.Published).Payload.ToArray());
        }

        [Fact]
        public async Task MqttPublisher_Publish_WhenBrokerDisconnected_ReturnsFalse()
        {
            var (connector, client) = await CreateConnectedAsync();
            var publisher = new MqttPublisher(connector, NullLogger<MqttPublisher>.Instance);
            await client.DisconnectAsync(new MQTTnet.MqttClientDisconnectOptions(), CancellationToken.None);

            var result = await publisher.Publish("some/topic", "payload");

            Assert.False(result);
            Assert.Empty(client.Published);
            connector.Dispose();
        }
    }
}
