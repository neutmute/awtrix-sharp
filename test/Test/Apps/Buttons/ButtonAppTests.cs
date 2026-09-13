using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Test.Apps.MqttRender;

namespace Test.Apps.Buttons
{
    public class ButtonAppTests
    {
        private Mock<ILogger> _mockLogger;
        private Mock<IAwtrixService> _mockAwtrixService;
        private Mock<IMqttConnector> _mockMqttConnector;
        private AwtrixAddress _address;

        private ButtonApp CreateSut()
        {
            _mockLogger = new Mock<ILogger>();
            _mockAwtrixService = new Mock<IAwtrixService>();
            _mockMqttConnector = new Mock<IMqttConnector>();
            _address = new AwtrixAddress { BaseTopic = "test/base/topic" };

            _mockMqttConnector.Setup(x => x.Subscribe(It.IsAny<string>())).Returns(Task.CompletedTask);

            var config = new AppConfig();

            return new ButtonApp(_mockLogger.Object, config, _address, _mockAwtrixService.Object, _mockMqttConnector.Object);
        }

        [Fact]
        public void Init_SubscribesToAllThreeButtonTopics()
        {
            var sut = CreateSut();

            sut.Init();

            _mockMqttConnector.Verify(x => x.Subscribe("test/base/topic/stats/buttonLeft"), Times.Once);
            _mockMqttConnector.Verify(x => x.Subscribe("test/base/topic/stats/buttonRight"), Times.Once);
            _mockMqttConnector.Verify(x => x.Subscribe("test/base/topic/stats/buttonSelect"), Times.Once);
        }

        [Fact]
        public void MessageReceived_ButtonPressed_RaisesClick()
        {
            var sut = CreateSut();
            sut.Init();
            ButtonEventArgs received = null;
            sut.Click += (s, e) => received = e;

            var args = MqttTestHelpers.CreateReceivedArgs("test/base/topic/stats/buttonLeft", "1");
            _mockMqttConnector.Raise(x => x.MessageReceived += null, new object[] { args });

            Assert.NotNull(received);
            Assert.Equal(Button.Left, received.Button);
        }

        [Fact]
        public void MessageReceived_UnrelatedTopic_DoesNotRaiseClick()
        {
            var sut = CreateSut();
            sut.Init();
            var clickCount = 0;
            sut.Click += (s, e) => clickCount++;

            var args = MqttTestHelpers.CreateReceivedArgs("some/other/topic", "1");
            _mockMqttConnector.Raise(x => x.MessageReceived += null, new object[] { args });

            Assert.Equal(0, clickCount);
        }

        [Fact]
        public void MessageReceived_PressReleasePressQuickly_RaisesDoubleClick()
        {
            var sut = CreateSut();
            sut.Init();
            var clickCount = 0;
            var doubleClickCount = 0;
            sut.Click += (s, e) => clickCount++;
            sut.DoubleClick += (s, e) => doubleClickCount++;

            var topic = "test/base/topic/stats/buttonSelect";
            _mockMqttConnector.Raise(x => x.MessageReceived += null, new object[] { MqttTestHelpers.CreateReceivedArgs(topic, "1") });
            _mockMqttConnector.Raise(x => x.MessageReceived += null, new object[] { MqttTestHelpers.CreateReceivedArgs(topic, "0") });
            _mockMqttConnector.Raise(x => x.MessageReceived += null, new object[] { MqttTestHelpers.CreateReceivedArgs(topic, "1") });

            Assert.Equal(1, clickCount);
            Assert.Equal(1, doubleClickCount);
        }
    }
}
