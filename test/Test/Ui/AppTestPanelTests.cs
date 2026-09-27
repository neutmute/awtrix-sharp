using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Ui.Pages;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Test.HostedServices;

namespace Test.Ui
{
    public class AppTestPanelTests : TestContext
    {
        private readonly Mock<IAwtrixService> _awtrix = new();
        private readonly Mock<IMqttConnector> _mqtt = new();
        private readonly PublishTrace _trace = new();
        private readonly Conductor _conductor;

        public AppTestPanelTests()
        {
            var config = new AwtrixConfig
            {
                Devices = new[]
                {
                    new DeviceConfig
                    {
                        BaseTopic = "awtrix/clock1",
                        Apps = new List<AppConfig>
                        {
                            new() { Type = "DiurnalApp", Config = new AppConfigKeys { ["0600"] = "Brightness=8" } },
                            new() { Type = "MqttRenderApp", Config = new AppConfigKeys { ["ReadTopic"] = "x/y" }, ValueMaps = new List<ValueMap> { new() { ValueMatcher = "^-", ["Icon"] = "redpower" } } },
                        },
                    },
                    new DeviceConfig { BaseTopic = "http://localhost:8080", Apps = new List<AppConfig> { new() { Type = "DiurnalApp" } } },
                },
            };
            _awtrix.Setup(a => a.Dismiss(It.IsAny<AwtrixAddress>())).ReturnsAsync(true);
            _awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>())).ReturnsAsync(false);
            _conductor = ConductorTestHelper.Create(config: config, awtrixService: _awtrix.Object, mqttConnector: _mqtt.Object);
            _conductor.RegisterApp(ConductorTestHelper.MockApp("awtrix/clock1", "DiurnalApp").Object);

            Services.AddSingleton(_conductor);
            Services.AddSingleton(Options.Create(config));
            Services.AddSingleton(_awtrix.Object);
            Services.AddSingleton(_mqtt.Object);
            Services.AddSingleton(_trace);
            Services.AddSingleton(new DeviceStateMonitor(_mqtt.Object, Options.Create(config), NullLogger<DeviceStateMonitor>.Instance));
        }

        [Fact]
        public void Renders_ADevicePerConfiguredDevice_WithItsApps()
        {
            var cut = RenderComponent<AppTestPanel>();

            var cards = cut.FindAll(".card.device");
            Assert.Equal(2, cards.Count);
            Assert.Contains("awtrix/clock1", cards[0].TextContent);
            Assert.Contains("MQTT", cards[0].TextContent);
            Assert.Contains("HTTP", cards[1].TextContent);
            Assert.Contains("MqttRenderApp", cards[0].TextContent);
            Assert.Contains("ReadTopic=x/y", cards[0].TextContent);
            Assert.Contains("^-", cards[0].TextContent);
            Assert.Contains("nothing sent yet", cards[0].TextContent);
        }

        [Fact]
        public void RunNow_ShowsStarted_ForRunningApp_AndNotRunning_Otherwise()
        {
            var cut = RenderComponent<AppTestPanel>();

            var buttons = cut.FindAll("button.run-now");
            buttons[0].Click();
            Assert.Contains("Started", cut.FindAll(".app-row")[0].TextContent);

            cut.FindAll("button.run-now")[1].Click();
            Assert.Contains("Not running", cut.FindAll(".app-row")[1].TextContent);
        }

        [Fact]
        public void LastPayload_AppearsAfterTraceRecord()
        {
            var cut = RenderComponent<AppTestPanel>();

            _trace.Record(new PublishRecord(DateTimeOffset.UtcNow, "awtrix/clock1", "AppUpdate", "MqttRenderApp", AwtrixRequest.Post("awtrix/clock1/cmd/app/MqttRenderApp", "{\"text\":\"42\"}"), true));

            cut.WaitForAssertion(() => Assert.Contains("\"text\": \"42\"", cut.FindAll(".app-row")[1].TextContent));
            Assert.Contains("42", cut.Find(".recent").TextContent);
        }

        [Fact]
        public void Dismiss_CallsService_AndShowsResult()
        {
            var cut = RenderComponent<AppTestPanel>();

            cut.FindAll("button.dismiss")[0].Click();

            _awtrix.Verify(a => a.Dismiss(It.Is<AwtrixAddress>(x => x.BaseTopic == "awtrix/clock1")), Times.Once);
            cut.WaitForAssertion(() => Assert.Contains("delivered", cut.FindAll(".card.device")[0].QuerySelector(".result")!.TextContent));
        }

        [Fact]
        public void ClearApp_UsesTypedName_AndReportsNotDelivered()
        {
            var cut = RenderComponent<AppTestPanel>();
            var card = cut.FindAll(".card.device")[0];
            card.QuerySelector("input.clear-name")!.Change("Custom");

            cut.FindAll("button.clear-app")[0].Click();

            _awtrix.Verify(a => a.AppClear(It.Is<AwtrixAddress>(x => x.BaseTopic == "awtrix/clock1"), "Custom"), Times.Once);
            cut.WaitForAssertion(() => Assert.Contains("not delivered", cut.FindAll(".card.device")[0].QuerySelector(".result")!.TextContent));
        }
    }
}
