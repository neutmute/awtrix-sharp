using AwtrixSharpWeb.Controllers;
using AwtrixSharpWeb.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Test.Controllers
{
    public class MqttControllerTests
    {
        [Fact]
        public async Task Publish_WhenConnectorSucceeds_ReturnsOk_AndOnlyPublishes()
        {
            var connector = new Mock<IMqttConnector>(MockBehavior.Strict);
            connector.Setup(c => c.PublishAsync("awtrix/clock1/notify", "{\"text\":\"hi\"}")).ReturnsAsync(true);
            var controller = new MqttController(connector.Object);

            var result = await controller.Publish("awtrix/clock1/notify", "{\"text\":\"hi\"}");

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("Message published", ok.Value);
            connector.Verify(c => c.PublishAsync("awtrix/clock1/notify", "{\"text\":\"hi\"}"), Times.Once);
            connector.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Publish_WhenConnectorFails_Returns503()
        {
            var connector = new Mock<IMqttConnector>();
            connector.Setup(c => c.PublishAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
            var controller = new MqttController(connector.Object);

            var result = await controller.Publish("awtrix/clock1/notify", "{}");

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(503, objectResult.StatusCode);
        }
    }
}
