using System.Buffers;
using System.Reflection;
using System.Text;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MQTTnet;

namespace Test.HostedServices
{
    /// <summary>
    /// Exercises MqttPublisher -> MqttConnector.PublishAsync end to end, without a real
    /// broker, by reflecting the private "_client" field on MqttConnector to a mocked
    /// MQTTnet IMqttClient. This is the only seam available since MqttConnector creates
    /// its real client only inside ConnectAsync() (which requires a live broker).
    /// </summary>
    public class MqttPublisherConnectorTests
    {
        private static MqttConnector CreateConnectorWithMockClient(Mock<IMqttClient> mockClient)
        {
            var connector = new MqttConnector(NullLogger<MqttConnector>.Instance, Options.Create(new MqttSettings()));

            var field = typeof(MqttConnector).GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            field!.SetValue(connector, mockClient.Object);

            return connector;
        }

        [Fact]
        public async Task Publish_SendsCorrectTopicAndPayloadToMqttClient()
        {
            var mockClient = new Mock<IMqttClient>();
            MqttApplicationMessage? captured = null;
            mockClient
                .Setup(c => c.PublishAsync(It.IsAny<MqttApplicationMessage>(), It.IsAny<CancellationToken>()))
                .Callback<MqttApplicationMessage, CancellationToken>((msg, _) => captured = msg)
                .ReturnsAsync((MqttClientPublishResult)null!);

            var connector = CreateConnectorWithMockClient(mockClient);
            var publisher = new MqttPublisher(connector, NullLogger<MqttPublisher>.Instance);

            var result = await publisher.Publish("awtrix/clock1/notify", "{\"text\":\"hi\"}");

            Assert.True(result);
            Assert.NotNull(captured);
            Assert.Equal("awtrix/clock1/notify", captured!.Topic);
            var payloadBytes = captured.Payload.ToArray();
            Assert.Equal("{\"text\":\"hi\"}", Encoding.UTF8.GetString(payloadBytes));
            mockClient.Verify(c => c.PublishAsync(It.IsAny<MqttApplicationMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Publish_WithEmptyPayload_SendsEmptyByteArray()
        {
            var mockClient = new Mock<IMqttClient>();
            MqttApplicationMessage? captured = null;
            mockClient
                .Setup(c => c.PublishAsync(It.IsAny<MqttApplicationMessage>(), It.IsAny<CancellationToken>()))
                .Callback<MqttApplicationMessage, CancellationToken>((msg, _) => captured = msg)
                .ReturnsAsync((MqttClientPublishResult)null!);

            var connector = CreateConnectorWithMockClient(mockClient);

            await connector.PublishAsync("awtrix/clock1/notify/dismiss", string.Empty);

            Assert.NotNull(captured);
            Assert.Empty(captured!.Payload.ToArray());
        }

        [Fact]
        public async Task MqttPublisher_Publish_ReturnsTrue()
        {
            var mockClient = new Mock<IMqttClient>();
            mockClient
                .Setup(c => c.PublishAsync(It.IsAny<MqttApplicationMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((MqttClientPublishResult)null!);

            var connector = CreateConnectorWithMockClient(mockClient);
            var publisher = new MqttPublisher(connector, NullLogger<MqttPublisher>.Instance);

            var result = await publisher.Publish("some/topic", "payload");

            Assert.True(result);
        }
    }
}
