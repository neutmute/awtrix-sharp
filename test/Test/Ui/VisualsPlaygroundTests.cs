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
using Test.Apps.MqttRender;

namespace Test.Ui
{
    public class VisualsPlaygroundTests : TestContext
    {
        private readonly Mock<IAwtrixService> _awtrix = new();
        private readonly Mock<IMqttConnector> _mqtt = new();
        private readonly DeviceStateMonitor _monitor;

        public VisualsPlaygroundTests()
        {
            var config = new AwtrixConfig
            {
                Devices = new[] { new DeviceConfig { BaseTopic = "awtrix/clock1" }, new DeviceConfig { BaseTopic = "http://localhost:8080" } },
            };
            _mqtt.Setup(m => m.Subscribe(It.IsAny<string>())).Returns(Task.CompletedTask);
            _monitor = new DeviceStateMonitor(_mqtt.Object, Options.Create(config), NullLogger<DeviceStateMonitor>.Instance);
            _monitor.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            _awtrix.Setup(a => a.Notify(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixAppMessage>())).ReturnsAsync(true);
            _awtrix.Setup(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>())).ReturnsAsync(true);

            Services.AddSingleton(Options.Create(config));
            Services.AddSingleton(_awtrix.Object);
            Services.AddSingleton(_mqtt.Object);
            Services.AddSingleton(new PublishTrace());
            Services.AddSingleton(_monitor);
        }

        [Fact]
        public void JsonPreview_FollowsForm()
        {
            var cut = RenderComponent<VisualsPlayground>();

            cut.Find("select.effect").Change("Plasma");

            Assert.Contains("\"effect\": \"Plasma\"", cut.Find("textarea.json").GetAttribute("value") ?? cut.Find("textarea.json").TextContent);
        }

        [Fact]
        public void Send_Notify_CallsService_WithBuiltMessage()
        {
            var cut = RenderComponent<VisualsPlayground>();
            cut.Find("select.effect").Change("Matrix");

            cut.Find("button.send").Click();

            _awtrix.Verify(a => a.Notify(It.Is<AwtrixAddress>(x => x.BaseTopic == "awtrix/clock1"), It.Is<AwtrixAppMessage>(m => (string)m["effect"]! == "Matrix")), Times.Once);
            cut.WaitForAssertion(() => Assert.Contains("delivered", cut.Find(".send-result").TextContent));
        }

        [Fact]
        public void Send_CustomApp_CallsAppUpdate_WithName()
        {
            var cut = RenderComponent<VisualsPlayground>();
            cut.Find("input.target-app").Change(true);
            cut.Find("input.app-name").Change("demo");

            cut.Find("button.send").Click();

            _awtrix.Verify(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), "demo", It.IsAny<AwtrixAppMessage>()), Times.Once);
        }

        [Fact]
        public void JsonEdit_UpdatesForm_AndKeepsUnknownKeys_AcrossFormEdits()
        {
            var cut = RenderComponent<VisualsPlayground>();

            cut.Find("textarea.json").Input("{\"text\":\"manual\",\"effect\":\"Snake\",\"foo\":1}");

            Assert.False(cut.Find("select.effect").HasAttribute("disabled"));
            Assert.Equal("Snake", cut.Find("select.effect").GetAttribute("value"));
            Assert.Equal("manual", cut.Find("input.text").GetAttribute("value"));
            cut.Find("button.send").Click();
            _awtrix.Verify(a => a.Notify(It.IsAny<AwtrixAddress>(), It.Is<AwtrixAppMessage>(m => m.Text == "manual" && m.ContainsKey("foo"))), Times.Once);

            cut.Find("input.text-color").Change("#00FF00");

            var json = cut.Find("textarea.json").GetAttribute("value") ?? cut.Find("textarea.json").TextContent;
            Assert.Contains("\"foo\": 1", json);
            Assert.Contains("\"effect\": \"Snake\"", json);
            Assert.Contains("\"textColor\": \"#00FF00\"", json);
        }

        [Fact]
        public void JsonEdit_BadJson_DisablesSendOnly_UntilFixed()
        {
            var cut = RenderComponent<VisualsPlayground>();

            cut.Find("textarea.json").Input("{\"text\":");

            Assert.True(cut.Find("button.send").HasAttribute("disabled"));
            Assert.Contains("Invalid JSON", cut.Find(".error").TextContent);
            Assert.False(cut.Find("select.effect").HasAttribute("disabled"));
            Assert.Empty(cut.FindAll("button.back-to-form"));

            cut.Find("textarea.json").Input("{\"text\":\"ok\"}");

            Assert.False(cut.Find("button.send").HasAttribute("disabled"));
            Assert.Empty(cut.FindAll(".error"));
        }

        [Fact]
        public void JsonEdit_BlankText_IsSent()
        {
            var cut = RenderComponent<VisualsPlayground>();

            cut.Find("textarea.json").Input("{\"text\":\"\"}");
            cut.Find("button.send").Click();

            // A blank text is sent through the normal path, where the service turns it into a Dismiss.
            _awtrix.Verify(a => a.Notify(It.IsAny<AwtrixAddress>(), It.Is<AwtrixAppMessage>(m => m.Text == null || m.Text == "")), Times.Once);
        }

        [Fact]
        public void Datalists_ComeFromDevice_WhenReceived_ElseBuiltIn()
        {
            var cut = RenderComponent<VisualsPlayground>();
            Assert.Contains("built-in lists", cut.Find(".caps-source").TextContent);

            _mqtt.Raise(m => m.MessageReceived += null, new object[] { MqttTestHelpers.CreateReceivedArgs("awtrix/clock1/state/capabilities", "{\"effects\":[\"OnlyOne\"]}") });

            cut.WaitForAssertion(() =>
            {
                Assert.Contains("lists from device", cut.Find(".caps-source").TextContent);
                Assert.Equal(2, cut.FindAll("select.effect option").Count); // (none) + OnlyOne
            });

            cut.Find("select.device").Change("http://localhost:8080");
            Assert.Contains("built-in lists", cut.Find(".caps-source").TextContent);
            Assert.Equal(NgVisuals.Effects.Length + 1, cut.FindAll("select.effect option").Count);
        }

        [Fact]
        public void Preset_LoadsForm()
        {
            var cut = RenderComponent<VisualsPlayground>();

            cut.Find("button.preset").Click();

            Assert.Equal("Plasma", cut.Find("select.effect").GetAttribute("value"));
        }

        [Fact]
        public void ValidationError_DisablesSend()
        {
            var cut = RenderComponent<VisualsPlayground>();

            cut.Find("input.text-color").Change("purple");

            Assert.True(cut.Find("button.send").HasAttribute("disabled"));
            Assert.Contains("Text colour", cut.Find(".error").TextContent);
        }

        [Fact]
        public void JsonEdit_SwitchingToCustomApp_KeepsEditedJson()
        {
            var cut = RenderComponent<VisualsPlayground>();

            cut.Find("textarea.json").Input("{\"text\":\"manual\"}");
            cut.Find("input.target-app").Change(true);
            cut.Find("button.send").Click();

            _awtrix.Verify(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.Is<AwtrixAppMessage>(m => m.Text == "manual")), Times.Once);
            var textarea = cut.Find("textarea.json");
            Assert.Contains("manual", textarea.GetAttribute("value") ?? textarea.TextContent);
        }

        [Fact]
        public void JsonEdit_CustomAppWithBlankName_DisablesSend_AndShowsError()
        {
            var cut = RenderComponent<VisualsPlayground>();

            cut.Find("textarea.json").Input("{\"text\":\"manual\"}");
            cut.Find("input.target-app").Change(true);
            cut.Find("input.app-name").Change("");

            Assert.True(cut.Find("button.send").HasAttribute("disabled"));
            Assert.Contains("App name is required", cut.Find(".error").TextContent);
        }

        [Fact]
        public void NoDevicesConfigured_DisablesSend()
        {
            Services.AddSingleton(Options.Create(new AwtrixConfig()));

            var cut = RenderComponent<VisualsPlayground>();

            Assert.True(cut.Find("button.send").HasAttribute("disabled"));
        }
    }
}
