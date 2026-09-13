using System.Buffers;
using System.Text;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Formatter;

namespace Test.HostedServices
{
    /// <summary>
    /// MqttConnector over a FakeMqttClient: one long-lived client, connector-owned handlers,
    /// subscription registry, honest publish results, safe start/stop. No broker, no sleeps.
    /// </summary>
    public class MqttConnectorTests
    {
        private static MqttConnector CreateConnector(FakeMqttClient client, MqttSettings? settings = null)
        {
            return new MqttConnector(
                NullLogger<MqttConnector>.Instance,
                Options.Create(settings ?? new MqttSettings()),
                client,
                // No real timers: a reconnect backoff waits until the connector is stopped or disposed.
                (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));
        }

        [Fact]
        public void BuildClientOptions_UsesConfiguredHostProtocolAndCredentials()
        {
            var options = MqttConnector.BuildClientOptions(new MqttSettings { Host = "broker.lan", Username = "user", Password = "secret" });

            var tcp = Assert.IsType<MqttClientTcpOptions>(options.ChannelOptions);
            Assert.Equal("Unspecified/broker.lan:1883", tcp.RemoteEndpoint.ToString());
            Assert.Equal(MqttProtocolVersion.V500, options.ProtocolVersion);
            Assert.Equal("user", options.Credentials.GetUserName(options));
            Assert.Equal("secret", Encoding.UTF8.GetString(options.Credentials.GetPassword(options)));
        }

        [Fact]
        public void BuildClientOptions_WithoutUsername_SendsNoCredentials()
        {
            var options = MqttConnector.BuildClientOptions(new MqttSettings { Host = "broker.lan" });

            Assert.Null(options.Credentials);
        }

        [Fact]
        public async Task StartAsync_ConnectsTheInjectedClient()
        {
            var client = new FakeMqttClient();
            var connector = CreateConnector(client);

            await connector.StartAsync(CancellationToken.None);

            Assert.True(client.IsConnected);
            Assert.Equal(1, client.ConnectCallCount);
        }

        [Fact]
        public async Task ConnectAsync_WhenAlreadyConnected_DoesNotConnectAgain()
        {
            var client = new FakeMqttClient();
            var connector = CreateConnector(client);
            await connector.StartAsync(CancellationToken.None);

            var result = await connector.ConnectAsync();

            Assert.True(result);
            Assert.Equal(1, client.ConnectCallCount);
        }

        [Fact]
        public async Task StartAsync_BrokerDown_CompletesWithoutThrowing()
        {
            var client = new FakeMqttClient();
            client.ConnectOutcomes.Enqueue(false);
            client.ConnectOutcomes.Enqueue(false);
            var connector = CreateConnector(client);

            var exception = await Record.ExceptionAsync(() => connector.StartAsync(CancellationToken.None));

            Assert.Null(exception);
            connector.Dispose();
        }

        [Fact]
        public async Task MessageReceived_HandlerAttachedBeforeStart_ReceivesMessages()
        {
            var client = new FakeMqttClient();
            var connector = CreateConnector(client);
            var received = new List<string>();
            connector.MessageReceived += args => { received.Add(args.ApplicationMessage.Topic); return Task.CompletedTask; };

            await connector.StartAsync(CancellationToken.None);
            await client.DeliverAsync("awtrix/clock1/stats/buttonLeft", "1");

            Assert.Equal(new[] { "awtrix/clock1/stats/buttonLeft" }, received);
        }

        [Fact]
        public async Task MessageReceived_ThrowingHandler_DoesNotStopOtherHandlers()
        {
            var client = new FakeMqttClient();
            var connector = CreateConnector(client);
            var secondCalls = 0;
            connector.MessageReceived += _ => throw new InvalidOperationException("boom");
            connector.MessageReceived += _ => { secondCalls++; return Task.CompletedTask; };
            await connector.StartAsync(CancellationToken.None);

            var exception = await Record.ExceptionAsync(() => client.DeliverAsync("some/topic", "x"));

            Assert.Null(exception);
            Assert.Equal(1, secondCalls);
        }

        [Fact]
        public async Task MessageReceived_RemovedHandler_IsNotInvoked()
        {
            var client = new FakeMqttClient();
            var connector = CreateConnector(client);
            var calls = 0;
            Func<MqttApplicationMessageReceivedEventArgs, Task> handler = _ => { calls++; return Task.CompletedTask; };
            connector.MessageReceived += handler;
            await connector.StartAsync(CancellationToken.None);

            connector.MessageReceived -= handler;
            await client.DeliverAsync("some/topic", "x");

            Assert.Equal(0, calls);
            Assert.Equal(1, client.MessageReceivedSubscriberCount);
        }

        [Fact]
        public async Task Subscribe_WhileConnected_SendsSubscribeImmediately()
        {
            var client = new FakeMqttClient();
            var connector = CreateConnector(client);
            await connector.StartAsync(CancellationToken.None);

            await connector.Subscribe("awtrix/clock1/stats/buttonLeft");

            Assert.Equal(new[] { "awtrix/clock1/stats/buttonLeft" }, client.SubscribeRequests);
        }

        [Fact]
        public async Task Subscribe_BeforeConnect_DoesNotThrow_AndIsAppliedOnConnect()
        {
            var client = new FakeMqttClient();
            var connector = CreateConnector(client);

            var exception = await Record.ExceptionAsync(() => connector.Subscribe("sensors/temp"));
            Assert.Null(exception);
            Assert.Empty(client.SubscribeRequests);

            await connector.StartAsync(CancellationToken.None);

            Assert.Equal(new[] { "sensors/temp" }, client.SubscribeRequests);
        }

        [Fact]
        public async Task Subscribe_SameTopicTwiceBeforeConnect_IsSubscribedOnceOnConnect()
        {
            var client = new FakeMqttClient();
            var connector = CreateConnector(client);

            await connector.Subscribe("sensors/temp");
            await connector.Subscribe("sensors/temp");
            await connector.StartAsync(CancellationToken.None);

            Assert.Equal(new[] { "sensors/temp" }, client.SubscribeRequests);
        }

        [Fact]
        public async Task Subscribe_KnownTopicWhileConnected_ResendsSoRetainedMessagesAreRedelivered()
        {
            var client = new FakeMqttClient();
            var connector = CreateConnector(client);
            await connector.StartAsync(CancellationToken.None);

            await connector.Subscribe("sensors/temp");
            await connector.Subscribe("sensors/temp");

            Assert.Equal(new[] { "sensors/temp", "sensors/temp" }, client.SubscribeRequests);
        }

        [Fact]
        public async Task Subscribe_WhenClientThrows_DoesNotThrow()
        {
            var client = new FakeMqttClient { SubscribeException = new InvalidOperationException("broker said no") };
            var connector = CreateConnector(client);
            await connector.StartAsync(CancellationToken.None);

            var exception = await Record.ExceptionAsync(() => connector.Subscribe("sensors/temp"));

            Assert.Null(exception);
        }

        [Fact]
        public async Task Subscribe_EmptyTopic_IsIgnored()
        {
            var client = new FakeMqttClient();
            var connector = CreateConnector(client);
            await connector.StartAsync(CancellationToken.None);

            await connector.Subscribe("");

            Assert.Empty(client.SubscribeRequests);
        }

        [Fact]
        public async Task PublishAsync_WhenConnected_SendsTopicAndPayload_AndReturnsTrue()
        {
            var client = new FakeMqttClient();
            var connector = CreateConnector(client);
            await connector.StartAsync(CancellationToken.None);

            var result = await connector.PublishAsync("awtrix/clock1/notify", "{\"text\":\"hi\"}");

            Assert.True(result);
            var message = Assert.Single(client.Published);
            Assert.Equal("awtrix/clock1/notify", message.Topic);
            Assert.Equal("{\"text\":\"hi\"}", Encoding.UTF8.GetString(message.Payload.ToArray()));
        }

        [Fact]
        public async Task PublishAsync_NullPayload_SendsEmptyPayload()
        {
            var client = new FakeMqttClient();
            var connector = CreateConnector(client);
            await connector.StartAsync(CancellationToken.None);

            var result = await connector.PublishAsync("awtrix/clock1/custom/app", null!);

            Assert.True(result);
            Assert.Empty(Assert.Single(client.Published).Payload.ToArray());
        }

        [Fact]
        public async Task PublishAsync_WhenDisconnected_ReturnsFalse_WithoutTouchingClient()
        {
            var client = new FakeMqttClient();
            var connector = CreateConnector(client);

            var result = await connector.PublishAsync("awtrix/clock1/notify", "{}");

            Assert.False(result);
            Assert.Empty(client.Published);
            Assert.Equal(0, client.ConnectCallCount);
        }

        [Fact]
        public async Task PublishAsync_WhenClientThrows_ReturnsFalse_AndDoesNotReconnectOrReplaceClient()
        {
            var client = new FakeMqttClient();
            var connector = CreateConnector(client);
            await connector.StartAsync(CancellationToken.None);
            client.PublishException = new InvalidOperationException("socket closed");

            var result = await connector.PublishAsync("awtrix/clock1/notify", "{}");

            Assert.False(result);
            Assert.Equal(1, client.ConnectCallCount);
        }

        [Fact]
        public async Task StopAsync_BeforeStart_DoesNotThrow_AndDisposesClient()
        {
            var client = new FakeMqttClient();
            var connector = CreateConnector(client);

            var exception = await Record.ExceptionAsync(() => connector.StopAsync(CancellationToken.None));

            Assert.Null(exception);
            Assert.True(client.Disposed);
        }

        [Fact]
        public async Task StopAsync_WhenConnected_DisconnectsThenDisposesClient()
        {
            var client = new FakeMqttClient();
            var connector = CreateConnector(client);
            await connector.StartAsync(CancellationToken.None);

            await connector.StopAsync(CancellationToken.None);

            Assert.Equal(1, client.DisconnectCallCount);
            Assert.True(client.Disposed);
        }

        [Fact]
        public async Task AfterStop_PublishAndSubscribe_ReturnWithoutThrowing()
        {
            var client = new FakeMqttClient();
            var connector = CreateConnector(client);
            await connector.StartAsync(CancellationToken.None);
            await connector.StopAsync(CancellationToken.None);

            Assert.False(await connector.PublishAsync("t", "p"));
            Assert.Null(await Record.ExceptionAsync(() => connector.Subscribe("t")));
            Assert.False(await connector.ConnectAsync());
            connector.Dispose();
        }
    }
}
