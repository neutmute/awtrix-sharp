using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Test.HostedServices
{
    /// <summary>
    /// CR-03 reconnect behaviour over a FakeMqttClient. The backoff delay is injected:
    /// it records the requested durations and completes immediately, so no test sleeps.
    /// </summary>
    public class MqttConnectorReconnectTests
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

        private readonly List<TimeSpan> _delays = new();

        private MqttConnector CreateConnector(FakeMqttClient client, Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            delay ??= (duration, token) =>
            {
                token.ThrowIfCancellationRequested();
                lock (_delays)
                {
                    _delays.Add(duration);
                }
                return Task.CompletedTask;
            };

            return new MqttConnector(
                NullLogger<MqttConnector>.Instance,
                Options.Create(new MqttSettings()),
                client,
                delay);
        }

        [Fact]
        public async Task BrokerRestart_HandlersAndSubscriptionsSurviveOnTheSameClient()
        {
            var client = new FakeMqttClient();
            var connector = CreateConnector(client);
            var received = new List<string>();
            connector.MessageReceived += args => { received.Add(args.ApplicationMessage.Topic); return Task.CompletedTask; };
            await connector.StartAsync(CancellationToken.None);
            await connector.Subscribe("awtrix/clock1/stats/buttonLeft");
            await connector.Subscribe("sensors/temp");
            client.SubscribeRequests.Clear();

            await client.SimulateConnectionLostAsync();
            await connector.ReconnectTask.WaitAsync(TestTimeout);

            Assert.True(client.IsConnected);
            Assert.Equal(2, client.ConnectCallCount);
            Assert.Equal(new[] { "awtrix/clock1/stats/buttonLeft", "sensors/temp" }, client.SubscribeRequests.OrderBy(t => t, StringComparer.Ordinal));

            await client.DeliverAsync("sensors/temp", "21.5");
            Assert.Equal(new[] { "sensors/temp" }, received);
            Assert.True(await connector.PublishAsync("awtrix/clock1/notify", "{}"));
        }

        [Fact]
        public async Task StartAsync_BrokerDown_RetriesWithExponentialBackoffUntilConnected()
        {
            var client = new FakeMqttClient();
            client.ConnectOutcomes.Enqueue(false); // StartAsync
            client.ConnectOutcomes.Enqueue(false); // retry 1
            client.ConnectOutcomes.Enqueue(false); // retry 2
            client.ConnectOutcomes.Enqueue(true);  // retry 3
            var connector = CreateConnector(client);
            await connector.Subscribe("sensors/temp");

            await connector.StartAsync(CancellationToken.None);
            await connector.ReconnectTask.WaitAsync(TestTimeout);

            Assert.True(client.IsConnected);
            Assert.Equal(4, client.ConnectCallCount); // a single loop despite a DisconnectedAsync per failed attempt
            Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) }, _delays);
            Assert.Equal(new[] { "sensors/temp" }, client.SubscribeRequests);
        }

        [Fact]
        public async Task Subscribe_FailureWhileConnected_IsRetriedOnReconnect()
        {
            var client = new FakeMqttClient();
            var connector = CreateConnector(client);
            await connector.StartAsync(CancellationToken.None);
            client.SubscribeException = new InvalidOperationException("broker said no");
            await connector.Subscribe("sensors/temp");
            client.SubscribeException = null;

            await client.SimulateConnectionLostAsync();
            await connector.ReconnectTask.WaitAsync(TestTimeout);

            Assert.Equal(new[] { "sensors/temp" }, client.SubscribeRequests);
        }

        [Fact]
        public async Task PublishAsync_WhileDisconnected_ReturnsFalse_ThenTrueAfterReconnect()
        {
            var client = new FakeMqttClient();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var connector = CreateConnector(client, async (_, token) => await gate.Task.WaitAsync(token));
            await connector.StartAsync(CancellationToken.None);

            await client.SimulateConnectionLostAsync();
            Assert.False(await connector.PublishAsync("awtrix/clock1/notify", "{}"));

            gate.SetResult();
            await connector.ReconnectTask.WaitAsync(TestTimeout);
            Assert.True(await connector.PublishAsync("awtrix/clock1/notify", "{}"));
        }

        [Fact]
        public async Task StopAsync_DuringBackoff_CancelsLoop_DoesNotReconnect_AndDisposesClient()
        {
            var client = new FakeMqttClient();
            var connector = CreateConnector(client, (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));
            await connector.StartAsync(CancellationToken.None);
            await client.SimulateConnectionLostAsync();
            var loop = connector.ReconnectTask;
            Assert.False(loop.IsCompleted);

            await connector.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);

            Assert.True(loop.IsCompleted);
            Assert.Equal(1, client.ConnectCallCount);
            Assert.True(client.Disposed);
        }

        [Fact]
        public async Task StopAsync_CleanDisconnect_DoesNotStartReconnectLoop()
        {
            var client = new FakeMqttClient();
            var connector = CreateConnector(client);
            await connector.StartAsync(CancellationToken.None);

            await connector.StopAsync(CancellationToken.None);

            Assert.True(connector.ReconnectTask.IsCompleted);
            Assert.Empty(_delays);
            Assert.Equal(1, client.ConnectCallCount);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 2)]
        [InlineData(2, 4)]
        [InlineData(5, 32)]
        [InlineData(6, 60)]
        [InlineData(50, 60)]
        public void GetReconnectDelay_DoublesPerAttempt_CappedAtSixtySeconds(int attempt, int expectedSeconds)
        {
            Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), MqttConnector.GetReconnectDelay(attempt));
        }
    }
}
