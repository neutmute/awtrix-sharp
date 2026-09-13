using System.Reflection;
using AwtrixSharpWeb.Apps.SlackStatus;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Test.Apps.SlackStatus
{
    /// <summary>
    /// SlackStatusApp's constructor takes the concrete AwtrixService (and SlackConnector) rather
    /// than the IAwtrixService/interfaces used by every other app, so it can't be given a mocked
    /// service. We build a real (never-connected) service chain instead. As long as
    /// SlackStatusAppConfig.Type is left unset, AwtrixApp.AppClear() short-circuits without
    /// touching the network, so the "no matching user" and "clear status" branches are safely
    /// testable. The "publish a non-empty status" branch would reach real MQTT/HTTP publishing
    /// code and is documented as a testability blocker in the final report instead of being
    /// exercised here.
    /// </summary>
    public class SlackStatusAppTests
    {
        private static AwtrixService CreateRealAwtrixService()
        {
            var mqttConnector = new MqttConnector(new Mock<ILogger<MqttConnector>>().Object, Options.Create(new MqttSettings()));
            var mqttPublisher = new MqttPublisher(mqttConnector, new Mock<ILogger<MqttPublisher>>().Object);
            var httpPublisher = new HttpPublisher(
                new Mock<ILogger<HttpPublisher>>().Object,
                new Test.Services.StubHttpClientFactory(Test.Services.StubHttpMessageHandler.Returning(System.Net.HttpStatusCode.OK)));
            return new AwtrixService(httpPublisher, mqttPublisher);
        }

        private static void RaiseUserStatusChanged(SlackConnector connector, SlackUserStatusChangedEventArgs args)
        {
            var field = typeof(SlackConnector).GetField("UserStatusChanged", BindingFlags.NonPublic | BindingFlags.Instance);
            var del = field?.GetValue(connector) as MulticastDelegate;
            del?.DynamicInvoke(connector, args);
        }

        private (SlackStatusApp sut, SlackConnector connector) CreateSut(string trackingUserId = "U123")
        {
            var logger = new Mock<ILogger>().Object;
            var connector = new SlackConnector(new Mock<ILogger<SlackConnector>>().Object);
            var address = new AwtrixAddress { BaseTopic = "test/base/topic" };
            var config = new SlackStatusAppConfig();
            config.Config.Add("SlackUserId", trackingUserId);
            var service = CreateRealAwtrixService();

            var sut = new SlackStatusApp(logger, config, address, service, connector);
            sut.Init();

            return (sut, connector);
        }

        [Fact]
        public void Init_DoesNotThrow_AndSubscribesToConnector()
        {
            var (sut, connector) = CreateSut();

            var ex = Record.Exception(() => RaiseUserStatusChanged(connector, new SlackUserStatusChangedEventArgs { UserId = "NoOneIsTrackingThis" }));

            Assert.Null(ex);
        }

        [Fact]
        public void UserStatusChanged_ForDifferentUser_IsIgnored()
        {
            var (sut, connector) = CreateSut(trackingUserId: "U123");

            var ex = Record.Exception(() => RaiseUserStatusChanged(connector, new SlackUserStatusChangedEventArgs
            {
                UserId = "SomeoneElse",
                StatusText = "In a meeting"
            }));

            Assert.Null(ex);
        }

        [Fact]
        public void UserStatusChanged_ForTrackedUser_WithEmptyStatus_ClearsWithoutThrowing()
        {
            var (sut, connector) = CreateSut(trackingUserId: "U123");

            // Config.Type/Name is left unset, so AppClear() short-circuits before touching the
            // network (see AwtrixApp.AppClear: "if (Config.Name == null) return false;").
            var ex = Record.Exception(() => RaiseUserStatusChanged(connector, new SlackUserStatusChangedEventArgs
            {
                UserId = "U123",
                StatusText = string.Empty
            }));

            Assert.Null(ex);
        }
    }
}
