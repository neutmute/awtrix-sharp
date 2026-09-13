using System.Reflection;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MQTTnet;

namespace Test.HostedServices
{
    /// <summary>
    /// Covers what is testable on MqttConnector without a real broker: behaviour once
    /// a client has been injected via reflection (Subscribe, event wiring) and the
    /// documented behaviour of calling operations before ConnectAsync has ever run.
    /// ConnectAsync itself always requires a live TCP broker and is out of scope for
    /// unit tests (see final report - testability blocker).
    /// </summary>
    public class MqttConnectorTests
    {
        private static MqttConnector CreateConnector()
        {
            return new MqttConnector(NullLogger<MqttConnector>.Instance, Options.Create(new MqttSettings()));
        }

        private static void InjectClient(MqttConnector connector, IMqttClient client)
        {
            var field = typeof(MqttConnector).GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            field!.SetValue(connector, client);
        }

        [Fact]
        public async Task Subscribe_CallsClientSubscribeAsyncWithGivenTopic()
        {
            var mockClient = new Mock<IMqttClient>();
            MqttClientSubscribeOptions? captured = null;
            mockClient
                .Setup(c => c.SubscribeAsync(It.IsAny<MqttClientSubscribeOptions>(), It.IsAny<CancellationToken>()))
                .Callback<MqttClientSubscribeOptions, CancellationToken>((opts, _) => captured = opts)
                .ReturnsAsync((MqttClientSubscribeResult)null!);

            var connector = CreateConnector();
            InjectClient(connector, mockClient.Object);

            await connector.Subscribe("awtrix/clock1/stats/buttonLeft");

            Assert.NotNull(captured);
            Assert.Contains(captured!.TopicFilters, f => f.Topic == "awtrix/clock1/stats/buttonLeft");
        }

        [Fact]
        public void MessageReceived_Add_AttachesToUnderlyingClientEvent()
        {
            var mockClient = new Mock<IMqttClient>();
            mockClient.SetupAdd(c => c.ApplicationMessageReceivedAsync += It.IsAny<Func<MqttApplicationMessageReceivedEventArgs, Task>>());

            var connector = CreateConnector();
            InjectClient(connector, mockClient.Object);

            Func<MqttApplicationMessageReceivedEventArgs, Task> handler = _ => Task.CompletedTask;
            connector.MessageReceived += handler;

            mockClient.VerifyAdd(c => c.ApplicationMessageReceivedAsync += It.IsAny<Func<MqttApplicationMessageReceivedEventArgs, Task>>(), Times.Once);
        }

        [Fact]
        public void MessageReceived_Remove_DetachesFromUnderlyingClientEvent()
        {
            var mockClient = new Mock<IMqttClient>();
            mockClient.SetupRemove(c => c.ApplicationMessageReceivedAsync -= It.IsAny<Func<MqttApplicationMessageReceivedEventArgs, Task>>());

            var connector = CreateConnector();
            InjectClient(connector, mockClient.Object);

            Func<MqttApplicationMessageReceivedEventArgs, Task> handler = _ => Task.CompletedTask;
            connector.MessageReceived += handler;
            connector.MessageReceived -= handler;

            mockClient.VerifyRemove(c => c.ApplicationMessageReceivedAsync -= It.IsAny<Func<MqttApplicationMessageReceivedEventArgs, Task>>(), Times.Once);
        }

        [Fact]
        public async Task StopAsync_BeforeAnyConnectAttempt_ThrowsBecauseClientNeverCreated()
        {
            // Documents current behaviour: MqttConnector.StopAsync calls _client.DisconnectAsync()
            // directly with no null-check. If StopAsync() runs before ConnectAsync() has ever
            // succeeded (e.g. host shutdown racing a slow/failed startup), this throws instead
            // of completing gracefully like a well-behaved IHostedService.StopAsync should.
            // (MQTTnet's DisconnectAsync extension method itself null-checks its "client"
            // argument, so the observed exception is ArgumentNullException rather than a
            // raw NullReferenceException - either way, StopAsync throws instead of no-op'ing.)
            var connector = CreateConnector();

            await Assert.ThrowsAsync<ArgumentNullException>(() => connector.StopAsync(CancellationToken.None));
        }

        [Fact]
        public async Task Subscribe_BeforeAnyConnectAttempt_ThrowsBecauseClientNeverCreated()
        {
            var connector = CreateConnector();

            await Assert.ThrowsAsync<ArgumentNullException>(() => connector.Subscribe("some/topic"));
        }

        // Note: PublishAsync's catch-block fallback (it calls ConnectAsync() again on failure,
        // which opens a real TCP connection) is not covered here - exercising it would require
        // a real broker/socket attempt, which is out of scope for these unit tests. See the
        // final report's testability blockers.
    }
}
