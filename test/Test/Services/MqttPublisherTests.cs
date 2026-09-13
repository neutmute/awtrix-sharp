using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Test.Services
{
    public class MqttPublisherTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Publish_ReturnsConnectorResult(bool connectorResult)
        {
            var connector = new Mock<IMqttConnector>();
            connector.Setup(c => c.PublishAsync("awtrix/clock1/notify", "{}")).ReturnsAsync(connectorResult);
            var publisher = new MqttPublisher(connector.Object, NullLogger<MqttPublisher>.Instance);

            var result = await publisher.Publish("awtrix/clock1/notify", "{}");

            Assert.Equal(connectorResult, result);
        }

        [Fact]
        public async Task Publish_WhenConnectorThrows_ReturnsFalseWithoutThrowing()
        {
            var connector = new Mock<IMqttConnector>();
            connector.Setup(c => c.PublishAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("client not connected"));
            var publisher = new MqttPublisher(connector.Object, NullLogger<MqttPublisher>.Instance);

            var result = await publisher.Publish("awtrix/clock1/notify", "{}");

            Assert.False(result);
        }
    }
}
