using AwtrixSharpWeb.Controllers;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Test.HostedServices;

namespace Test.Controllers
{
    /// <summary>
    /// MqttRenderController.StartNow() just forwards to Conductor.ExecuteNow(). We use a
    /// real Conductor (see ConductorTestHelper) targeting an unknown device so the guard
    /// clause returns early - this exercises the controller's plumbing without touching
    /// any real MQTT/HTTP publish path.
    /// </summary>
    public class MqttRenderControllerTests
    {
        [Fact]
        public void StartNow_ReturnsOkWithMessageMentioningAppAndDevice()
        {
            var conductor = ConductorTestHelper.Create();
            var controller = new MqttRenderController(conductor);

            var result = controller.StartNow("awtrix/clock1", "MqttRenderApp");

            var okResult = Assert.IsType<OkObjectResult>(result);
            var message = okResult.Value!.ToString();
            Assert.Contains("MqttRenderApp", message);
            Assert.Contains("awtrix/clock1", message);
        }

        [Fact]
        public void StartNow_UsesDefaultParameters_WhenNoneSupplied()
        {
            var conductor = ConductorTestHelper.Create();
            var controller = new MqttRenderController(conductor);

            var result = controller.StartNow();

            Assert.IsType<OkObjectResult>(result);
        }
    }
}
