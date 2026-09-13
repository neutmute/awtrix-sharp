using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging;
using Moq;
using MQTTnet;

namespace Test.Apps.MqttRender
{
    public class MqttRenderAppTests
    {
        private Mock<ILogger> _mockLogger;
        private Mock<IAwtrixService> _mockAwtrixService;
        private Mock<IMqttConnector> _mockMqttConnector;
        private AwtrixAddress _address;
        private MockClock _clock;
        private MqttAppConfig _config;

        private AwtrixSharpWeb.Apps.MqttRender.MqttRenderApp CreateSut(string readTopic = "read/topic")
        {
            _mockLogger = new Mock<ILogger>();
            _mockAwtrixService = new Mock<IAwtrixService>();
            _mockMqttConnector = new Mock<IMqttConnector>();
            _address = new AwtrixAddress { BaseTopic = "test/base/topic" };
            _clock = new MockClock(DateTimeOffset.Now);

            _mockAwtrixService.Setup(x => x.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>())).ReturnsAsync(true);
            _mockAwtrixService.Setup(x => x.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>())).ReturnsAsync(true);
            _mockAwtrixService.Setup(x => x.Dismiss(It.IsAny<AwtrixAddress>())).ReturnsAsync(true);
            _mockMqttConnector.Setup(x => x.Subscribe(It.IsAny<string>())).Returns(Task.CompletedTask);

            _config = new MqttAppConfig
            {
                CronSchedule = "* * * * *",
                ActiveTime = TimeSpan.FromMinutes(5),
                ReadTopic = readTopic
            };
            _config.WithName("MqttRenderApp");

            return new AwtrixSharpWeb.Apps.MqttRender.MqttRenderApp(
                _mockLogger.Object, _clock, _config, _address, _mockAwtrixService.Object, _mockMqttConnector.Object);
        }

        [Fact]
        public void ExecuteNow_SubscribesToConfiguredTopic()
        {
            var sut = CreateSut("read/topic");

            sut.ExecuteNow();

            _mockMqttConnector.Verify(x => x.Subscribe("read/topic"), Times.Once);

            sut.Dispose();
        }

        [Fact]
        public void MessageReceived_OnMatchingTopic_UpdatesAppWithText()
        {
            var sut = CreateSut("read/topic");
            sut.ExecuteNow();

            var args = MqttTestHelpers.CreateReceivedArgs("read/topic", "hello world");
            _mockMqttConnector.Raise(x => x.MessageReceived += null, new object[] { args });

            _mockAwtrixService.Verify(x => x.AppUpdate(
                _address,
                "MqttRenderApp",
                It.Is<AwtrixAppMessage>(m => m.Text == "hello world")), Times.Once);

            sut.Dispose();
        }

        [Fact]
        public void MessageReceived_OnDifferentTopic_IsIgnored()
        {
            var sut = CreateSut("read/topic");
            sut.ExecuteNow();

            var args = MqttTestHelpers.CreateReceivedArgs("other/topic", "hello world");
            _mockMqttConnector.Raise(x => x.MessageReceived += null, new object[] { args });

            _mockAwtrixService.Verify(x => x.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()), Times.Never);

            sut.Dispose();
        }

        [Fact]
        public void MessageReceived_WithMatchingValueMap_DecoratesMessage()
        {
            var sut = CreateSut("read/topic");
            _config.ValueMaps.Add(new ValueMap { { "ValueMatcher", "busy" }, { "Icon", "12345" } });
            sut.ExecuteNow();

            var args = MqttTestHelpers.CreateReceivedArgs("read/topic", "busy");
            _mockMqttConnector.Raise(x => x.MessageReceived += null, new object[] { args });

            _mockAwtrixService.Verify(x => x.AppUpdate(
                _address,
                "MqttRenderApp",
                It.Is<AwtrixAppMessage>(m => m["icon"] == "12345")), Times.Once);

            sut.Dispose();
        }

        [Fact]
        public void MessageReceived_WithValueMapThatSetsNoText_FallsBackToRawPayload()
        {
            var sut = CreateSut("read/topic");
            _config.ValueMaps.Add(new ValueMap { { "ValueMatcher", "busy" }, { "Icon", "12345" } });
            sut.ExecuteNow();

            var args = MqttTestHelpers.CreateReceivedArgs("read/topic", "busy");
            _mockMqttConnector.Raise(x => x.MessageReceived += null, new object[] { args });

            _mockAwtrixService.Verify(x => x.AppUpdate(
                _address,
                "MqttRenderApp",
                It.Is<AwtrixAppMessage>(m => m.Text == "busy")), Times.Once);

            sut.Dispose();
        }
    }
}
