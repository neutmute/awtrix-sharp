using AwtrixSharpWeb.Controllers;
using AwtrixSharpWeb.HostedServices;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Test.HostedServices;

namespace Test.Controllers
{
    /// <summary>
    /// MqttRenderController maps Conductor.ExecuteNow's result to 200/404/500. Uses a real Conductor
    /// (ConductorTestHelper) with mocked apps registered through RegisterApp.
    /// </summary>
    public class MqttRenderControllerTests
    {
        [Fact]
        public void StartNow_RunningApp_ReturnsOkWithMessageMentioningAppAndDevice()
        {
            var conductor = ConductorTestHelper.Create();
            var app = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.MqttRenderApp);
            conductor.RegisterApp(app.Object);
            var controller = new MqttRenderController(conductor);

            var result = controller.StartNow("awtrix/clock1", AppNames.MqttRenderApp);

            var okResult = Assert.IsType<OkObjectResult>(result);
            var message = okResult.Value!.ToString();
            Assert.Contains("MqttRenderApp", message);
            Assert.Contains("awtrix/clock1", message);
            app.Verify(a => a.ExecuteNow(), Times.Once);
        }

        [Fact]
        public void StartNow_UsesDefaultParameters_WhenNoneSupplied()
        {
            var conductor = ConductorTestHelper.Create();
            conductor.RegisterApp(ConductorTestHelper.MockApp("awtrix/clock1", AppNames.MqttRenderApp).Object);
            var controller = new MqttRenderController(conductor);

            var result = controller.StartNow();

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public void StartNow_AppNotRunningOnDevice_Returns404()
        {
            var conductor = ConductorTestHelper.Create();
            var controller = new MqttRenderController(conductor);

            var result = controller.StartNow("awtrix/clock1", AppNames.MqttRenderApp);

            var notFound = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Contains("awtrix/clock1", notFound.Value!.ToString());
        }

        [Fact]
        public void StartNow_AppThrows_Returns500()
        {
            var conductor = ConductorTestHelper.Create();
            var app = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.MqttRenderApp);
            app.Setup(a => a.ExecuteNow()).Throws(new InvalidOperationException("boom"));
            conductor.RegisterApp(app.Object);
            var controller = new MqttRenderController(conductor);

            var result = controller.StartNow("awtrix/clock1", AppNames.MqttRenderApp);

            Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
        }
    }
}
