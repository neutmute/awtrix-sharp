using AwtrixSharpWeb.Controllers;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Test.HostedServices;
using Test.Services;

namespace Test.Controllers
{
    /// <summary>
    /// Covers the DiagnosticsController actions that don't require a real MQTT broker.
    /// AwtrixService is built with FakeHttpPublisher/FakeMqttPublisher (see
    /// Services/FakePublishers.cs) so device notifications never hit real network.
    /// The controller depends on IMqttConnector, so the "mqtt" endpoint uses a Moq fake.
    /// </summary>
    public class DiagnosticsControllerTests
    {
        private static (DiagnosticsController controller, FakeHttpPublisher http, FakeMqttPublisher mqtt) CreateController(AwtrixConfig? config = null)
            => CreateController(new Mock<IMqttConnector>(), config);

        private static (DiagnosticsController controller, FakeHttpPublisher http, FakeMqttPublisher mqtt) CreateController(Mock<IMqttConnector> mqttConnector, AwtrixConfig? config = null)
        {
            config ??= new AwtrixConfig { Devices = Array.Empty<DeviceConfig>() };
            var http = new FakeHttpPublisher();
            var mqtt = new FakeMqttPublisher();
            var awtrixService = new AwtrixService(http, mqtt);
            var conductor = ConductorTestHelper.Create(config);

            var controller = new DiagnosticsController(
                NullLogger<DiagnosticsController>.Instance,
                Options.Create(config),
                mqttConnector.Object,
                awtrixService,
                conductor);

            return (controller, http, mqtt);
        }

        [Fact]
        public void Get_ReturnsOkWithMessage()
        {
            var (controller, _, _) = CreateController();

            var result = controller.Get();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task Mqtt_PublishesDiagnosticPayloadViaConnector()
        {
            var connector = new Mock<IMqttConnector>();
            connector.Setup(c => c.PublishAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            var (controller, _, _) = CreateController(connector);

            var result = await controller.Mqtt();

            Assert.IsType<OkObjectResult>(result);
            connector.Verify(c => c.PublishAsync("awtrixsharp/diagnostic", It.Is<string>(p => p.Contains("Diagnostic test"))), Times.Once);
        }

        [Fact]
        public async Task AwtrixText_NoDevices_ReturnsOkWithoutPublishing()
        {
            var (controller, http, mqtt) = CreateController(new AwtrixConfig { Devices = Array.Empty<DeviceConfig>() });

            var result = await controller.AwtrixText("hello");

            Assert.IsType<OkResult>(result);
            Assert.Equal(0, http.PublishCallCount);
            Assert.Equal(0, mqtt.PublishCallCount);
        }

        [Fact]
        public async Task AwtrixText_WithDevice_DismissesThenNotifies()
        {
            var device = new DeviceConfig { BaseTopic = "awtrix/clock1" };
            var (controller, _, mqtt) = CreateController(new AwtrixConfig { Devices = new[] { device } });

            var result = await controller.AwtrixText("Hello World");

            Assert.IsType<OkResult>(result);
            // Dismiss() then Notify() -> two publishes; last one is the notify with our text.
            Assert.Equal(2, mqtt.PublishCallCount);
            Assert.Equal("awtrix/clock1/notify", mqtt.LastUrl);
            Assert.Contains("Hello World", mqtt.LastPayload);
        }

        [Fact]
        public async Task AwtrixText_SkipsDevicesWithEmptyBaseTopic()
        {
            var device = new DeviceConfig { BaseTopic = "" };
            var (controller, _, mqtt) = CreateController(new AwtrixConfig { Devices = new[] { device } });

            await controller.AwtrixText("Hello");

            Assert.Equal(0, mqtt.PublishCallCount);
        }

        [Fact]
        public async Task AwtrixProgess_WithDevice_PublishesProgressNotification()
        {
            var device = new DeviceConfig { BaseTopic = "awtrix/clock1" };
            var (controller, _, mqtt) = CreateController(new AwtrixConfig { Devices = new[] { device } });

            var result = await controller.AwtrixProgess(80);

            Assert.IsType<OkResult>(result);
            Assert.Equal(1, mqtt.PublishCallCount);
            Assert.Contains("\"progress\":\"80\"", mqtt.LastPayload);
        }

        [Fact]
        public async Task AwtrixRtttl_WithDevice_PlaysRtttlOnEachDevice()
        {
            var device1 = new DeviceConfig { BaseTopic = "awtrix/clock1" };
            var device2 = new DeviceConfig { BaseTopic = "awtrix/clock2" };
            var (controller, _, mqtt) = CreateController(new AwtrixConfig { Devices = new[] { device1, device2 } });

            var result = await controller.AwtrixRtttl("d=4,o=5,b=140:8g");

            Assert.IsType<OkResult>(result);
            Assert.Equal(2, mqtt.PublishCallCount);
            Assert.Equal("d=4,o=5,b=140:8g", mqtt.LastPayload);
        }

        [Fact]
        public async Task SetGlobalTextColor_PublishesSettingsToEachDevice()
        {
            var device = new DeviceConfig { BaseTopic = "awtrix/clock1" };
            var (controller, _, mqtt) = CreateController(new AwtrixConfig { Devices = new[] { device } });

            var result = await controller.SetGlobalTextColor("#123456");

            Assert.IsType<OkResult>(result);
            Assert.Equal("awtrix/clock1/settings", mqtt.LastUrl);
            Assert.Equal("{\"TCOL\":\"#123456\"}", mqtt.LastPayload);
        }
    }
}
