# WS2 MQTT Connector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One long-lived MQTT client whose handlers and subscriptions survive broker restarts, a background reconnect with backoff, honest non-throwing publish and subscribe, and a `/Mqtt/publish` endpoint that can no longer destroy the connection.

**Architecture:**
- `MqttConnector` creates its `IMqttClient` once and owns two things: the `MessageReceived` handler list and a topic registry.
  - The registry is replayed after every successful `ConnectAsync`.
  - `DisconnectedAsync` starts a single reconnect loop with exponential backoff (1 s doubling, 60 s cap).
- An internal constructor injects the client and the backoff delay, so all of this is unit-tested with a hand-rolled `FakeMqttClient`.
- `MqttRenderApp` attaches its handler before subscribing.
- `MqttController` depends on `IMqttConnector`, drops the reconnect call, and returns 503 on failure.

**Tech Stack:** .NET 10, ASP.NET Core, MQTTnet 5.0.1.1416, xUnit 2.9, Moq 4.20

**Spec:** `docs/superpowers/specs/2026-09-13-ws2-mqtt-connector-design.md`

## Global Constraints

- **Prerequisite:** WS1 (`docs/superpowers/plans/2026-09-13-ws1-runtime-resilience.md`) must be fully committed first. Do not start while another agent is still committing WS1.
- **Config compatibility:** no `appsettings.json` keys or `AWTRIXSHARP_*` env vars are added, renamed or reinterpreted. `Mqtt:Host`, `Mqtt:Username` and `Mqtt:Password` keep their meaning.
- **Client options unchanged:** `WithTcpServer(Host)`, `MqttProtocolVersion.V500`, credentials only when `Username` is non-empty.
- **No new configuration.** Code constants: `ConnectTimeout = 10 s`, and backoff `min(2^min(attempt,6), 60)` seconds.
- **No package changes.** MQTTnet stays at `5.0.1.1416`.
- **`IMqttConnector` member signatures are unchanged:**
  - `event Func<MqttApplicationMessageReceivedEventArgs, Task> MessageReceived`
  - `Task Subscribe(string topic)`
  - `Task<bool> PublishAsync(string topic, string payload)`
- **Keep** the public `MqttConnector(ILogger<MqttConnector>, IOptions<MqttSettings>)` constructor.
- **No real sleeps in tests.** Inject the delay. `WaitAsync(TimeSpan.FromSeconds(5))` is allowed only as a failure-mode guard.
- **Out of scope:** WS3 (InitAsync, `ButtonApp` `.Wait()`, Conductor) and WS4 (ScheduledApp, MqttClockRenderApp unsubscribe). Do not edit `Conductor.cs`, `ButtonApp.cs` or `Program.cs`.
- **TDD per task:**
  1. Write the failing test.
  2. Run it filtered; it fails.
  3. Implement.
  4. Run it filtered; it passes.
  5. Run the full `dotnet test` with 0 failed.
- **Commits:** explicit `git add <paths>`, never `-A` or `.`. Every commit message ends with exactly:
  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt
  ```
- Run every command from the repo root `C:\CodeMine\awtrix-sharp`.

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/api/Interfaces/IMqttConnector.cs` | contract docs (signatures unchanged) | 1 |
| `src/api/HostedServices/MqttConnector.cs` | single client, handler + subscription registries, publish, start/stop (T1); reconnect loop (T2) | 1, 2 |
| `test/Test/HostedServices/FakeMqttClient.cs` | `IMqttClient` fake mirroring MQTTnet 5 behaviour | 1 |
| `test/Test/HostedServices/MqttConnectorTests.cs` | rewritten connector tests | 1 |
| `test/Test/HostedServices/MqttPublisherConnectorTests.cs` | rewritten: internal ctor instead of reflection | 1 |
| `test/Test/HostedServices/MqttConnectorReconnectTests.cs` | broker restart, backoff, stop during reconnect | 2 |
| `src/api/Apps/MqttRender/MqttRenderApp.cs` | attach handler before subscribe | 3 |
| `test/Test/Apps/MqttRender/MqttRenderAppTests.cs`, `test/Test/Apps/MqttRender/MqttClockRenderAppTests.cs` | retained-message-during-subscribe tests | 3 |
| `src/api/Controllers/MqttController.cs` | `IMqttConnector`, no ConnectAsync, 503 | 4 |
| `test/Test/Controllers/MqttControllerTests.cs` | controller tests | 4 |

---

### Task 0: Verify WS1 seams exist as assumed

**Files:** none modified.

- [ ] **Step 1: Check the assumed WS1 shapes**

Run each command. Expected result is shown after the arrow.
- `git log --oneline -8` → WS1 commits are present, including `refactor(di): interface seams for Conductor and apps; validated composition root` (WS1 Task 5).
- `git status --short` → clean. If it is not clean, stop: another agent is still working.
- `grep -n "PublishAsync\|Subscribe\|MessageReceived" src/api/Interfaces/IMqttConnector.cs` → `event Func<MqttApplicationMessageReceivedEventArgs, Task> MessageReceived;`, `Task Subscribe(string topic);`, `Task<bool> PublishAsync(string topic, string payload);`
- `grep -n "public MqttPublisher" src/api/Services/MqttPublisher.cs` → `public MqttPublisher(IMqttConnector mqttConnector, ILogger<MqttPublisher> logger)`
- `grep -n "IMqttConnector" src/api/Program.cs` → `services.AddSingleton<IMqttConnector>(sp => sp.GetRequiredService<MqttConnector>());`
- `grep -n "Task<bool> PublishAsync" src/api/HostedServices/MqttConnector.cs` → one match (WS1 Task 2).
- `grep -rn "new MqttConnector(" test src` → only the public two-argument form `(logger, Options.Create(new MqttSettings()))` (FakePublishers, DiagnosticsControllerTests, SlackStatusAppTests, etc.). All of these keep compiling.
- `grep -n "InternalsVisibleTo" src/api/awtrix-api.csproj` → `<InternalsVisibleTo Include="Test" />`
- `grep -n "void Init\|Task InitAsync" src/api/Apps/AwtrixApp.cs` → records whether WS3 renamed `Init`. WS2 tests do not call `Init`.

- [ ] **Step 2: Adapt if WS1 differs**

- If a name above differs (for example the interface method is `SubscribeAsync`, or `MqttPublisher` takes a differently named parameter), use the landed name everywhere this plan uses the assumed one. Record the mapping in the Task 1 commit body.
- Do not change WS1's public signatures to match this plan.
- Baseline: run `dotnet test` and note the pass count (expected 0 failed).

---

### Task 1: Single long-lived client with connector-owned handlers and a subscription registry (CR-03 core)

**Files:**
- Create: `test/Test/HostedServices/FakeMqttClient.cs`
- Modify (full rewrite): `test/Test/HostedServices/MqttConnectorTests.cs`, `test/Test/HostedServices/MqttPublisherConnectorTests.cs`, `src/api/Interfaces/IMqttConnector.cs`, `src/api/HostedServices/MqttConnector.cs`

**Interfaces:**
- Consumes:
  - WS1 `IMqttConnector` (three members above)
  - `MqttPublisher(IMqttConnector, ILogger<MqttPublisher>)`
  - `Test.Apps.MqttRender.MqttTestHelpers.CreateReceivedArgs(string topic, string payload)` (existing)
- Produces:
  - `public MqttConnector(ILogger<MqttConnector> logger, IOptions<MqttSettings> settings)`
  - `internal MqttConnector(ILogger<MqttConnector> logger, IOptions<MqttSettings> settings, IMqttClient client)` (Task 2 appends an optional `Func<TimeSpan, CancellationToken, Task>? delay = null`)
  - `internal static readonly TimeSpan MqttConnector.ConnectTimeout` (10 s)
  - `internal static MqttClientOptions MqttConnector.BuildClientOptions(MqttSettings settings)`
  - `public Task<bool> MqttConnector.ConnectAsync(CancellationToken cancellationToken = default)` (never throws)
  - `MqttConnector : IHostedService, IMqttConnector, IDisposable`
  - `internal sealed class Test.HostedServices.FakeMqttClient : IMqttClient` with:
    - `Queue<bool> ConnectOutcomes`, `int ConnectCallCount`, `int DisconnectCallCount`, `bool Disposed`
    - `List<string> SubscribeRequests`, `List<MqttApplicationMessage> Published`
    - `Exception? PublishException`, `Exception? SubscribeException`
    - `int MessageReceivedSubscriberCount`
    - `Task SimulateConnectionLostAsync()`, `Task DeliverAsync(string topic, string payload)`

- [ ] **Step 1: Create the fake client**

`test/Test/HostedServices/FakeMqttClient.cs`:

```csharp
using MQTTnet;
using MQTTnet.Diagnostics.PacketInspection;
using MQTTnet.Exceptions;
using Test.Apps.MqttRender;

namespace Test.HostedServices
{
    /// <summary>
    /// Hand-rolled IMqttClient for connector tests. Mirrors the MQTTnet 5 behaviours the connector
    /// depends on (verified against MQTTnet 5.0.1.1416):
    /// a failed ConnectAsync raises DisconnectedAsync(clientWasConnected: false) and then throws;
    /// Publish/Subscribe while disconnected throw MqttClientNotConnectedException;
    /// DisconnectAsync raises DisconnectedAsync(clientWasConnected: true);
    /// ConnectAsync after Dispose throws ObjectDisposedException.
    /// </summary>
    internal sealed class FakeMqttClient : IMqttClient
    {
        private readonly object _gate = new();

        /// <summary>Outcome of each ConnectAsync call in order (true = success). Empty queue = success.</summary>
        public Queue<bool> ConnectOutcomes { get; } = new();
        public int ConnectCallCount { get; private set; }
        public int DisconnectCallCount { get; private set; }
        public bool Disposed { get; private set; }
        public List<string> SubscribeRequests { get; } = new();
        public List<MqttApplicationMessage> Published { get; } = new();
        public Exception? PublishException { get; set; }
        public Exception? SubscribeException { get; set; }

        public bool IsConnected { get; private set; }
        public MqttClientOptions Options { get; private set; } = null!;

        public event Func<MqttApplicationMessageReceivedEventArgs, Task>? ApplicationMessageReceivedAsync;
        public event Func<MqttClientConnectedEventArgs, Task>? ConnectedAsync;
        public event Func<MqttClientConnectingEventArgs, Task>? ConnectingAsync;
        public event Func<MqttClientDisconnectedEventArgs, Task>? DisconnectedAsync;
        public event Func<InspectMqttPacketEventArgs, Task>? InspectPacketAsync;

        public int MessageReceivedSubscriberCount => ApplicationMessageReceivedAsync?.GetInvocationList().Length ?? 0;

        public async Task<MqttClientConnectResult> ConnectAsync(MqttClientOptions options, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            bool succeed;
            lock (_gate)
            {
                ConnectCallCount++;
                Options = options;
                succeed = ConnectOutcomes.Count == 0 || ConnectOutcomes.Dequeue();
            }

            if (!succeed)
            {
                var error = new MqttCommunicationException("Connection refused");
                await RaiseDisconnected(clientWasConnected: false, MqttClientDisconnectReason.UnspecifiedError, error);
                throw error;
            }

            IsConnected = true;
            var result = new MqttClientConnectResult();
            if (ConnectedAsync != null)
            {
                await ConnectedAsync(new MqttClientConnectedEventArgs(result));
            }
            return result;
        }

        public async Task DisconnectAsync(MqttClientDisconnectOptions options, CancellationToken cancellationToken)
        {
            DisconnectCallCount++;
            if (!IsConnected)
            {
                return;
            }
            IsConnected = false;
            await RaiseDisconnected(clientWasConnected: true, MqttClientDisconnectReason.NormalDisconnection, null);
        }

        /// <summary>Simulates the broker going away (restart, network loss, keep-alive timeout).</summary>
        public Task SimulateConnectionLostAsync()
        {
            IsConnected = false;
            return RaiseDisconnected(clientWasConnected: true, MqttClientDisconnectReason.ServerShuttingDown, null);
        }

        /// <summary>Simulates the broker delivering a message on a subscribed topic.</summary>
        public Task DeliverAsync(string topic, string payload)
        {
            var handler = ApplicationMessageReceivedAsync;
            return handler == null ? Task.CompletedTask : handler(MqttTestHelpers.CreateReceivedArgs(topic, payload));
        }

        public Task<MqttClientPublishResult> PublishAsync(MqttApplicationMessage applicationMessage, CancellationToken cancellationToken)
        {
            if (!IsConnected)
            {
                throw new MqttClientNotConnectedException();
            }
            if (PublishException != null)
            {
                throw PublishException;
            }
            lock (_gate)
            {
                Published.Add(applicationMessage);
            }
            return Task.FromResult(new MqttClientPublishResult(null, MqttClientPublishReasonCode.Success, null, Array.Empty<MQTTnet.Packets.MqttUserProperty>()));
        }

        public Task<MqttClientSubscribeResult> SubscribeAsync(MqttClientSubscribeOptions options, CancellationToken cancellationToken)
        {
            if (!IsConnected)
            {
                throw new MqttClientNotConnectedException();
            }
            if (SubscribeException != null)
            {
                throw SubscribeException;
            }
            lock (_gate)
            {
                SubscribeRequests.AddRange(options.TopicFilters.Select(f => f.Topic));
            }
            return Task.FromResult(new MqttClientSubscribeResult(0, Array.Empty<MqttClientSubscribeResultItem>(), null, Array.Empty<MQTTnet.Packets.MqttUserProperty>()));
        }

        public Task<MqttClientUnsubscribeResult> UnsubscribeAsync(MqttClientUnsubscribeOptions options, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task PingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendEnhancedAuthenticationExchangeDataAsync(MqttEnhancedAuthenticationExchangeData data, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Dispose()
        {
            Disposed = true;
            IsConnected = false;
        }

        private Task RaiseDisconnected(bool clientWasConnected, MqttClientDisconnectReason reason, Exception? exception)
        {
            var handler = DisconnectedAsync;
            return handler == null
                ? Task.CompletedTask
                : handler(new MqttClientDisconnectedEventArgs(clientWasConnected, null, reason, null, null, exception));
        }
    }
}
```

The fake declares `ConnectingAsync` and `InspectPacketAsync` but never raises them. If the compiler reports warning CS0067 for those two events, that is expected and harmless. Do not suppress warnings project-wide.

- [ ] **Step 2: Rewrite the connector tests (replace the whole file)**

`test/Test/HostedServices/MqttConnectorTests.cs`:

```csharp
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
                client);
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
```

- [ ] **Step 3: Rewrite the publisher-to-connector tests (replace the whole file)**

`test/Test/HostedServices/MqttPublisherConnectorTests.cs`:

```csharp
using System.Buffers;
using System.Text;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Test.HostedServices
{
    /// <summary>
    /// MqttPublisher -> MqttConnector.PublishAsync end to end over a FakeMqttClient
    /// (injected through the connector's internal constructor; no reflection, no broker).
    /// </summary>
    public class MqttPublisherConnectorTests
    {
        private static async Task<(MqttConnector connector, FakeMqttClient client)> CreateConnectedAsync()
        {
            var client = new FakeMqttClient();
            var connector = new MqttConnector(NullLogger<MqttConnector>.Instance, Options.Create(new MqttSettings()), client);
            await connector.StartAsync(CancellationToken.None);
            return (connector, client);
        }

        [Fact]
        public async Task Publish_SendsCorrectTopicAndPayloadToMqttClient()
        {
            var (connector, client) = await CreateConnectedAsync();
            var publisher = new MqttPublisher(connector, NullLogger<MqttPublisher>.Instance);

            var result = await publisher.Publish("awtrix/clock1/notify", "{\"text\":\"hi\"}");

            Assert.True(result);
            var message = Assert.Single(client.Published);
            Assert.Equal("awtrix/clock1/notify", message.Topic);
            Assert.Equal("{\"text\":\"hi\"}", Encoding.UTF8.GetString(message.Payload.ToArray()));
        }

        [Fact]
        public async Task Publish_WithEmptyPayload_SendsEmptyByteArray()
        {
            var (connector, client) = await CreateConnectedAsync();

            await connector.PublishAsync("awtrix/clock1/notify/dismiss", string.Empty);

            Assert.Empty(Assert.Single(client.Published).Payload.ToArray());
        }

        [Fact]
        public async Task MqttPublisher_Publish_WhenBrokerDisconnected_ReturnsFalse()
        {
            var (connector, client) = await CreateConnectedAsync();
            var publisher = new MqttPublisher(connector, NullLogger<MqttPublisher>.Instance);
            await client.DisconnectAsync(new MQTTnet.MqttClientDisconnectOptions(), CancellationToken.None);

            var result = await publisher.Publish("some/topic", "payload");

            Assert.False(result);
            Assert.Empty(client.Published);
            connector.Dispose();
        }
    }
}
```

The last test disconnects the fake client directly. In Task 2 that raises `DisconnectedAsync` and starts a reconnect loop with the default 1 s real delay. `connector.Dispose()` cancels the loop, which is why the test ends with it.

- [ ] **Step 4: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.HostedServices.MqttConnectorTests|FullyQualifiedName~Test.HostedServices.MqttPublisherConnectorTests"`
Expected: build FAILS with `CS1729: 'MqttConnector' does not contain a constructor that takes 3 arguments` and `CS0117: 'MqttConnector' does not contain a definition for 'BuildClientOptions'`.

- [ ] **Step 5: Document the contract on IMqttConnector (replace the whole file)**

`src/api/Interfaces/IMqttConnector.cs`:

```csharp
using MQTTnet;

namespace AwtrixSharpWeb.Interfaces
{
    public interface IMqttConnector
    {
        /// <summary>
        /// Raised for every message received on any subscribed topic. Handlers are owned by the
        /// connector (not the underlying client), so they survive reconnects. A throwing handler
        /// is logged and does not prevent other handlers from running.
        /// </summary>
        event Func<MqttApplicationMessageReceivedEventArgs, Task> MessageReceived;

        /// <summary>
        /// Records the topic in the subscription registry and subscribes immediately when connected.
        /// While disconnected the subscription is deferred and applied on the next connect.
        /// Never throws.
        /// </summary>
        Task Subscribe(string topic);

        /// <summary>
        /// Returns true when the client accepted the publish; false (never throws) otherwise,
        /// including while disconnected. Failed publishes are not queued.
        /// </summary>
        Task<bool> PublishAsync(string topic, string payload);
    }
}
```

- [ ] **Step 6: Rebuild MqttConnector around one client (replace the whole file)**

`src/api/HostedServices/MqttConnector.cs`:

```csharp
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Formatter;
using System.Text;

namespace AwtrixSharpWeb.HostedServices
{
    /// <summary>
    /// Owns the single, long-lived MQTT client for the process.
    /// <list type="bullet">
    /// <item>The client is created once and reconnected in place; it is never replaced.</item>
    /// <item>Message handlers and topic subscriptions are held here, so they survive reconnects.</item>
    /// <item>No public member throws for broker/network faults.</item>
    /// </list>
    /// </summary>
    public class MqttConnector : IHostedService, IMqttConnector, IDisposable
    {
        internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

        private readonly IMqttClient _client;
        private readonly MqttClientOptions _clientOptions;
        private readonly ILogger<MqttConnector> _log;
        private readonly MqttSettings _settings;
        private readonly SemaphoreSlim _connectLock = new(1, 1);
        private readonly object _registryLock = new();
        private readonly HashSet<string> _topics = new(StringComparer.Ordinal);
        private Func<MqttApplicationMessageReceivedEventArgs, Task>? _messageHandlers;
        private int _disposed;

        public event Func<MqttApplicationMessageReceivedEventArgs, Task> MessageReceived
        {
            add { lock (_registryLock) { _messageHandlers += value; } }
            remove { lock (_registryLock) { _messageHandlers -= value; } }
        }

        public MqttConnector(ILogger<MqttConnector> logger, IOptions<MqttSettings> settings)
            : this(logger, settings, new MqttClientFactory().CreateMqttClient())
        {
        }

        /// <summary>
        /// Test seam: inject the client so connector behaviour runs without a broker.
        /// </summary>
        internal MqttConnector(ILogger<MqttConnector> logger, IOptions<MqttSettings> settings, IMqttClient client)
        {
            _log = logger;
            _settings = settings.Value;
            _client = client;
            _clientOptions = BuildClientOptions(_settings);

            _client.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
        }

        private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        internal static MqttClientOptions BuildClientOptions(MqttSettings settings)
        {
            var builder = new MqttClientOptionsBuilder()
                .WithTcpServer(settings.Host)
                .WithProtocolVersion(MqttProtocolVersion.V500);

            if (!string.IsNullOrEmpty(settings.Username))
            {
                builder.WithCredentials(settings.Username, settings.Password);
            }

            return builder.Build();
        }

        /// <summary>
        /// Connects the one client if it is not already connected, then replays the subscription registry.
        /// Returns false (never throws) on failure, timeout, cancellation or after disposal.
        /// </summary>
        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (IsDisposed)
            {
                return false;
            }

            try
            {
                await _connectLock.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            try
            {
                if (_client.IsConnected)
                {
                    return true;
                }

                _log.LogDebug("Connecting to MQTT broker at {Host}...", _settings.Host);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(ConnectTimeout);

                await _client.ConnectAsync(_clientOptions, timeoutCts.Token);

                if (!_client.IsConnected)
                {
                    _log.LogWarning("MQTT broker at {Host} did not accept the connection", _settings.Host);
                    return false;
                }

                _log.LogInformation("Connected to MQTT broker at {Host}", _settings.Host);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _log.LogWarning("Connection to MQTT broker at {Host} timed out after {Timeout}", _settings.Host, ConnectTimeout);
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Failed to connect to MQTT broker at {Host}: {Error}", _settings.Host, ex.Message);
                return false;
            }
            finally
            {
                _connectLock.Release();
            }

            await ReplaySubscriptionsAsync();
            return _client.IsConnected;
        }

        public async Task Subscribe(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic))
            {
                _log.LogWarning("Ignoring MQTT subscribe with an empty topic");
                return;
            }

            lock (_registryLock)
            {
                _topics.Add(topic);
            }

            if (IsDisposed || !_client.IsConnected)
            {
                _log.LogDebug("MQTT not connected; subscription to {Topic} will be applied on connect", topic);
                return;
            }

            // Always send SUBSCRIBE, even for a known topic: the broker re-sends retained
            // messages, which MqttRenderApp relies on at each activation.
            await SendSubscribeAsync(topic);
        }

        public async Task<bool> PublishAsync(string topic, string payload)
        {
            payload ??= string.Empty;

            if (IsDisposed || !_client.IsConnected)
            {
                _log.LogDebug("MQTT not connected; dropping publish to {Topic}", topic);
                return false;
            }

            _log.LogDebug("Publishing MQTT to topic {Topic} with payload {Payload}", topic, payload.Length == 0 ? "<empty>" : payload);

            try
            {
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(Encoding.UTF8.GetBytes(payload))
                    .Build();

                var result = await _client.PublishAsync(message, CancellationToken.None);

                if (result is { IsSuccess: false })
                {
                    _log.LogWarning("MQTT publish to {Topic} was rejected: {ReasonCode}", topic, result.ReasonCode);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                // No reconnect here: reconnecting is owned by the DisconnectedAsync handler (Task 2).
                _log.LogWarning("MQTT publish to {Topic} failed: {Error}", topic, ex.Message);
                return false;
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (!await ConnectAsync(cancellationToken))
            {
                _log.LogWarning("MQTT broker at {Host} unavailable at startup", _settings.Host);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (IsDisposed)
            {
                return;
            }

            if (_client.IsConnected)
            {
                try
                {
                    await _client.DisconnectAsync(new MqttClientDisconnectOptions(), cancellationToken);
                }
                catch (Exception ex)
                {
                    _log.LogWarning("MQTT disconnect during shutdown failed: {Error}", ex.Message);
                }
            }

            Dispose();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _client.ApplicationMessageReceivedAsync -= OnApplicationMessageReceivedAsync;
            _client.Dispose();
        }

        private async Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
        {
            Func<MqttApplicationMessageReceivedEventArgs, Task>? handlers;
            lock (_registryLock)
            {
                handlers = _messageHandlers;
            }

            if (handlers == null)
            {
                return;
            }

            foreach (var handler in handlers.GetInvocationList().Cast<Func<MqttApplicationMessageReceivedEventArgs, Task>>())
            {
                try
                {
                    await handler(args);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "MQTT message handler {Handler} failed for topic {Topic}",
                        $"{handler.Method.DeclaringType?.Name}.{handler.Method.Name}", args.ApplicationMessage?.Topic);
                }
            }
        }

        private async Task ReplaySubscriptionsAsync()
        {
            string[] topics;
            lock (_registryLock)
            {
                topics = _topics.ToArray();
            }

            foreach (var topic in topics)
            {
                await SendSubscribeAsync(topic);
            }

            if (topics.Length > 0)
            {
                _log.LogInformation("Subscribed to {Count} MQTT topic(s) after connect", topics.Length);
            }
        }

        private async Task SendSubscribeAsync(string topic)
        {
            try
            {
                var options = new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(topic)
                    .Build();

                await _client.SubscribeAsync(options, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _log.LogWarning("MQTT subscribe to {Topic} failed; it will be retried on the next connect: {Error}", topic, ex.Message);
            }
        }
    }
}
```

Notes:
- `ILogger<>` and `IHostedService` come from the Web SDK implicit usings, which the WS1-era file already relied on.
- If the compiler reports a missing namespace, add only that `using`.

- [ ] **Step 7: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.HostedServices.MqttConnectorTests|FullyQualifiedName~Test.HostedServices.MqttPublisherConnectorTests"`
Expected: PASS. 21 connector tests plus 3 publisher tests, 0 failed.

- [ ] **Step 8: Run the full suite**

Run: `dotnet test`
Expected: 0 failed. `DiagnosticsControllerTests`, `SlackStatusAppTests` and `FakePublishers` still build: they use the public two-argument constructor, which now creates a client but opens no socket.

- [ ] **Step 9: Commit**

```bash
git add test/Test/HostedServices/FakeMqttClient.cs test/Test/HostedServices/MqttConnectorTests.cs test/Test/HostedServices/MqttPublisherConnectorTests.cs src/api/Interfaces/IMqttConnector.cs src/api/HostedServices/MqttConnector.cs
git commit -m "fix(mqtt): single long-lived client with handler and subscription registries

CR-03 (part 1): MqttConnector creates its IMqttClient once and never
replaces it. MessageReceived handlers are owned by the connector and
invoked in isolation; subscribed topics are recorded and replayed after
every connect; Subscribe never throws while disconnected; PublishAsync
returns false while disconnected or on failure and no longer reconnects
from the publish path; Start/Stop/Dispose are safe before connect.
Tests use a FakeMqttClient via an internal constructor (no reflection).

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 2: Background reconnect loop with exponential backoff (CR-03 reconnect)

**Files:**
- Create: `test/Test/HostedServices/MqttConnectorReconnectTests.cs`
- Modify (full rewrite): `src/api/HostedServices/MqttConnector.cs`

**Interfaces:**
- Consumes (Task 1):
  - `FakeMqttClient` (`ConnectOutcomes`, `ConnectCallCount`, `SubscribeRequests`, `SubscribeException`, `Disposed`, `SimulateConnectionLostAsync()`, `DeliverAsync(...)`)
  - `MqttConnector` constructors
  - `ConnectAsync`
- Produces:
  - `internal MqttConnector(ILogger<MqttConnector> logger, IOptions<MqttSettings> settings, IMqttClient client, Func<TimeSpan, CancellationToken, Task>? delay = null)`. The three-argument call sites from Task 1 still compile.
  - `internal static readonly TimeSpan MqttConnector.MaxReconnectDelay` (60 s)
  - `internal static TimeSpan MqttConnector.GetReconnectDelay(int attempt)`
  - `internal Task MqttConnector.ReconnectTask` (completed when no loop is running)

- [ ] **Step 1: Write the failing reconnect tests**

`test/Test/HostedServices/MqttConnectorReconnectTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.HostedServices.MqttConnectorReconnectTests"`
Expected: build FAILS with `CS1729: 'MqttConnector' does not contain a constructor that takes 4 arguments`, `CS1061: 'MqttConnector' does not contain a definition for 'ReconnectTask'` and `CS0117: ... 'GetReconnectDelay'`.

- [ ] **Step 3: Add the reconnect loop (replace the whole file)**

`src/api/HostedServices/MqttConnector.cs`:

```csharp
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Formatter;
using System.Text;

namespace AwtrixSharpWeb.HostedServices
{
    /// <summary>
    /// Owns the single, long-lived MQTT client for the process.
    /// <list type="bullet">
    /// <item>The client is created once and reconnected in place; it is never replaced.</item>
    /// <item>Message handlers and topic subscriptions are held here, so they survive reconnects.</item>
    /// <item>A lost connection is retried in the background with exponential backoff.</item>
    /// <item>No public member throws for broker/network faults.</item>
    /// </list>
    /// </summary>
    public class MqttConnector : IHostedService, IMqttConnector, IDisposable
    {
        internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
        internal static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(60);

        private readonly IMqttClient _client;
        private readonly MqttClientOptions _clientOptions;
        private readonly ILogger<MqttConnector> _log;
        private readonly MqttSettings _settings;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly SemaphoreSlim _connectLock = new(1, 1);
        private readonly object _registryLock = new();
        private readonly HashSet<string> _topics = new(StringComparer.Ordinal);
        private readonly CancellationTokenSource _stopping = new();
        private Func<MqttApplicationMessageReceivedEventArgs, Task>? _messageHandlers;
        private Task _reconnectTask = Task.CompletedTask;
        private int _reconnectLoopRunning;
        private int _disposed;

        public event Func<MqttApplicationMessageReceivedEventArgs, Task> MessageReceived
        {
            add { lock (_registryLock) { _messageHandlers += value; } }
            remove { lock (_registryLock) { _messageHandlers -= value; } }
        }

        public MqttConnector(ILogger<MqttConnector> logger, IOptions<MqttSettings> settings)
            : this(logger, settings, new MqttClientFactory().CreateMqttClient())
        {
        }

        /// <summary>
        /// Test seam: inject the client and the backoff delay so reconnect behaviour runs without a broker or real time.
        /// </summary>
        internal MqttConnector(
            ILogger<MqttConnector> logger,
            IOptions<MqttSettings> settings,
            IMqttClient client,
            Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _log = logger;
            _settings = settings.Value;
            _client = client;
            _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
            _clientOptions = BuildClientOptions(_settings);

            _client.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
            _client.DisconnectedAsync += OnDisconnectedAsync;
        }

        /// <summary>The current background reconnect loop (completed when none is running). For tests and shutdown.</summary>
        internal Task ReconnectTask => Volatile.Read(ref _reconnectTask);

        private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        internal static MqttClientOptions BuildClientOptions(MqttSettings settings)
        {
            var builder = new MqttClientOptionsBuilder()
                .WithTcpServer(settings.Host)
                .WithProtocolVersion(MqttProtocolVersion.V500);

            if (!string.IsNullOrEmpty(settings.Username))
            {
                builder.WithCredentials(settings.Username, settings.Password);
            }

            return builder.Build();
        }

        /// <summary>1 s, 2 s, 4 s ... doubling per attempt, capped at <see cref="MaxReconnectDelay"/>.</summary>
        internal static TimeSpan GetReconnectDelay(int attempt)
        {
            var seconds = Math.Pow(2, Math.Clamp(attempt, 0, 6));
            return TimeSpan.FromSeconds(Math.Min(seconds, MaxReconnectDelay.TotalSeconds));
        }

        /// <summary>
        /// Connects the one client if it is not already connected, then replays the subscription registry.
        /// Returns false (never throws) on failure, timeout, cancellation or after disposal.
        /// </summary>
        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (IsDisposed)
            {
                return false;
            }

            try
            {
                await _connectLock.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            try
            {
                if (_client.IsConnected)
                {
                    return true;
                }

                _log.LogDebug("Connecting to MQTT broker at {Host}...", _settings.Host);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(ConnectTimeout);

                await _client.ConnectAsync(_clientOptions, timeoutCts.Token);

                if (!_client.IsConnected)
                {
                    _log.LogWarning("MQTT broker at {Host} did not accept the connection", _settings.Host);
                    return false;
                }

                _log.LogInformation("Connected to MQTT broker at {Host}", _settings.Host);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _log.LogWarning("Connection to MQTT broker at {Host} timed out after {Timeout}", _settings.Host, ConnectTimeout);
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Failed to connect to MQTT broker at {Host}: {Error}", _settings.Host, ex.Message);
                return false;
            }
            finally
            {
                _connectLock.Release();
            }

            await ReplaySubscriptionsAsync();
            return _client.IsConnected;
        }

        public async Task Subscribe(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic))
            {
                _log.LogWarning("Ignoring MQTT subscribe with an empty topic");
                return;
            }

            lock (_registryLock)
            {
                _topics.Add(topic);
            }

            if (IsDisposed || !_client.IsConnected)
            {
                _log.LogDebug("MQTT not connected; subscription to {Topic} will be applied on connect", topic);
                return;
            }

            // Always send SUBSCRIBE, even for a known topic: the broker re-sends retained
            // messages, which MqttRenderApp relies on at each activation.
            await SendSubscribeAsync(topic);
        }

        public async Task<bool> PublishAsync(string topic, string payload)
        {
            payload ??= string.Empty;

            if (IsDisposed || !_client.IsConnected)
            {
                _log.LogDebug("MQTT not connected; dropping publish to {Topic}", topic);
                return false;
            }

            _log.LogDebug("Publishing MQTT to topic {Topic} with payload {Payload}", topic, payload.Length == 0 ? "<empty>" : payload);

            try
            {
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(Encoding.UTF8.GetBytes(payload))
                    .Build();

                var result = await _client.PublishAsync(message, CancellationToken.None);

                if (result is { IsSuccess: false })
                {
                    _log.LogWarning("MQTT publish to {Topic} was rejected: {ReasonCode}", topic, result.ReasonCode);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                // No reconnect here: a dropped connection raises DisconnectedAsync, which owns reconnecting.
                _log.LogWarning("MQTT publish to {Topic} failed: {Error}", topic, ex.Message);
                return false;
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (!await ConnectAsync(cancellationToken))
            {
                _log.LogWarning("MQTT broker at {Host} unavailable at startup; retrying in the background", _settings.Host);
                EnsureReconnectLoop();
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (IsDisposed)
            {
                return;
            }

            _stopping.Cancel();

            try
            {
                await ReconnectTask.WaitAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
                _log.LogDebug("Stopped waiting for the MQTT reconnect loop");
            }

            if (_client.IsConnected)
            {
                try
                {
                    await _client.DisconnectAsync(new MqttClientDisconnectOptions(), cancellationToken);
                }
                catch (Exception ex)
                {
                    _log.LogWarning("MQTT disconnect during shutdown failed: {Error}", ex.Message);
                }
            }

            Dispose();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _stopping.Cancel();
            _client.ApplicationMessageReceivedAsync -= OnApplicationMessageReceivedAsync;
            _client.DisconnectedAsync -= OnDisconnectedAsync;
            _client.Dispose();
        }

        private async Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
        {
            Func<MqttApplicationMessageReceivedEventArgs, Task>? handlers;
            lock (_registryLock)
            {
                handlers = _messageHandlers;
            }

            if (handlers == null)
            {
                return;
            }

            foreach (var handler in handlers.GetInvocationList().Cast<Func<MqttApplicationMessageReceivedEventArgs, Task>>())
            {
                try
                {
                    await handler(args);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "MQTT message handler {Handler} failed for topic {Topic}",
                        $"{handler.Method.DeclaringType?.Name}.{handler.Method.Name}", args.ApplicationMessage?.Topic);
                }
            }
        }

        private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
        {
            if (_stopping.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            if (args.ClientWasConnected)
            {
                _log.LogWarning("Disconnected from MQTT broker at {Host} ({Reason}); reconnecting in the background",
                    _settings.Host, args.Reason);
            }

            // Never await reconnect work inside MQTTnet's event pipeline.
            EnsureReconnectLoop();
            return Task.CompletedTask;
        }

        private void EnsureReconnectLoop()
        {
            if (_stopping.IsCancellationRequested)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _reconnectLoopRunning, 1, 0) != 0)
            {
                return;
            }

            Volatile.Write(ref _reconnectTask, Task.Run(RunReconnectLoopAsync));
        }

        private async Task RunReconnectLoopAsync()
        {
            var token = _stopping.Token;
            var faulted = false;

            try
            {
                for (var attempt = 0; !token.IsCancellationRequested && !_client.IsConnected; attempt++)
                {
                    await _delay(GetReconnectDelay(attempt), token);

                    if (await ConnectAsync(token))
                    {
                        _log.LogInformation("Reconnected to MQTT broker at {Host} after {Attempts} attempt(s)", _settings.Host, attempt + 1);
                        break;
                    }

                    _log.LogWarning("MQTT reconnect attempt {Attempt} to {Host} failed; next attempt in {Delay}",
                        attempt + 1, _settings.Host, GetReconnectDelay(attempt + 1));
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown.
            }
            catch (Exception ex)
            {
                faulted = true;
                _log.LogError(ex, "MQTT reconnect loop stopped unexpectedly");
            }
            finally
            {
                Interlocked.Exchange(ref _reconnectLoopRunning, 0);
            }

            // A disconnect that raced the end of the loop must not be lost.
            if (!faulted && !token.IsCancellationRequested && !IsDisposed && !_client.IsConnected)
            {
                EnsureReconnectLoop();
            }
        }

        private async Task ReplaySubscriptionsAsync()
        {
            string[] topics;
            lock (_registryLock)
            {
                topics = _topics.ToArray();
            }

            foreach (var topic in topics)
            {
                await SendSubscribeAsync(topic);
            }

            if (topics.Length > 0)
            {
                _log.LogInformation("Subscribed to {Count} MQTT topic(s) after connect", topics.Length);
            }
        }

        private async Task SendSubscribeAsync(string topic)
        {
            try
            {
                var options = new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(topic)
                    .Build();

                await _client.SubscribeAsync(options, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _log.LogWarning("MQTT subscribe to {Topic} failed; it will be retried on the next connect: {Error}", topic, ex.Message);
            }
        }
    }
}
```

- [ ] **Step 4: Run the connector tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.HostedServices.MqttConnector|FullyQualifiedName~Test.HostedServices.MqttPublisherConnectorTests"`
Expected: PASS, 0 failed. That is 12 reconnect tests (6 facts plus 6 theory cases), 21 connector tests and 3 publisher tests.

Then repeat that command twice more. Expected: 0 failed each time, which confirms there are no timing races.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: 0 failed.

- [ ] **Step 6: Commit**

```bash
git add test/Test/HostedServices/MqttConnectorReconnectTests.cs src/api/HostedServices/MqttConnector.cs
git commit -m "fix(mqtt): reconnect in the background with exponential backoff

CR-03 (part 2): DisconnectedAsync and a failed startup connect start a
single background reconnect loop (1s doubling, 60s cap) on the same
client; subscriptions are replayed on reconnect and handlers survive.
StopAsync cancels the loop, disconnects and disposes the client. The
backoff delay is injected so tests run without a broker or real time.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 3: MqttRenderApp attaches its handler before subscribing (CR-33)

**Files:**
- Modify: `src/api/Apps/MqttRender/MqttRenderApp.cs` (the first two lines of `ActivateScheduledWork`)
- Modify: `test/Test/Apps/MqttRender/MqttRenderAppTests.cs` (append one test)
- Modify: `test/Test/Apps/MqttRender/MqttClockRenderAppTests.cs` (append one test)

**Interfaces:**
- Consumes: `IMqttConnector.Subscribe`, `IMqttConnector.MessageReceived`, and the existing test fields `_mockMqttConnector`, `_mockAwtrixService`, `_address`, plus `CreateSut(string readTopic)`.
- Produces: no new types.

- [ ] **Step 1: Write the failing tests**

In `test/Test/Apps/MqttRender/MqttRenderAppTests.cs`, add this method inside `public class MqttRenderAppTests`, after `MessageReceived_WithValueMapThatSetsNoText_FallsBackToRawPayload`:

```csharp
        [Fact]
        public void ExecuteNow_RetainedMessageDeliveredDuringSubscribe_IsRendered()
        {
            var sut = CreateSut("read/topic");
            // Simulate the broker delivering a retained message before Subscribe returns (CR-33)
            _mockMqttConnector
                .Setup(x => x.Subscribe("read/topic"))
                .Callback<string>(topic => _mockMqttConnector.Raise(
                    x => x.MessageReceived += null,
                    new object[] { MqttTestHelpers.CreateReceivedArgs(topic, "retained value") }))
                .Returns(Task.CompletedTask);

            sut.ExecuteNow();

            _mockAwtrixService.Verify(x => x.AppUpdate(
                _address,
                "MqttRenderApp",
                It.Is<AwtrixAppMessage>(m => m.Text == "retained value")), Times.Once);

            sut.Dispose();
        }
```

In `test/Test/Apps/MqttRender/MqttClockRenderAppTests.cs`, add this method inside `public class MqttClockRenderAppTests`, after `MessageReceived_SetsLongDuration`:

```csharp
        [Fact]
        public void ExecuteNow_RetainedMessageDeliveredDuringSubscribe_IsRendered()
        {
            var sut = CreateSut("read/topic");
            _mockMqttConnector
                .Setup(x => x.Subscribe("read/topic"))
                .Callback<string>(topic => _mockMqttConnector.Raise(
                    x => x.MessageReceived += null,
                    new object[] { MqttTestHelpers.CreateReceivedArgs(topic, "22.5C") }))
                .Returns(Task.CompletedTask);

            sut.ExecuteNow();

            _mockAwtrixService.Verify(x => x.AppUpdate(
                _address,
                "MqttClockRenderApp",
                It.Is<AwtrixAppMessage>(m => m.Text != null && m.Text.EndsWith(" 22.5C"))), Times.Once);

            sut.Dispose();
        }
```

Notes on these tests:
- `Raise` must receive `new object[] { args }`. Passing `args` directly binds to the `EventArgs` overload and throws `TargetParameterCountException`; this was verified with Moq 4.20.72.
- `ExecuteNow` runs synchronously up to `WaitForCancellation`, because the mocks return completed tasks, so `Verify` can follow directly. That is the same assumption the existing tests in these files make.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~ExecuteNow_RetainedMessageDeliveredDuringSubscribe_IsRendered"`
Expected: 2 FAIL with `Moq.MockException: Expected invocation on the mock once, but was 0 times`. The handler is attached after `Subscribe`, so the raised message has no subscriber.

- [ ] **Step 3: Attach before subscribing**

In `src/api/Apps/MqttRender/MqttRenderApp.cs`, replace

```csharp
            await _mqttConnector.Subscribe(Config.ReadTopic);
            _mqttConnector.MessageReceived += RawMessageReceived;
```

with

```csharp
            // Attach first: a retained message can arrive before Subscribe returns (CR-33)
            _mqttConnector.MessageReceived += RawMessageReceived;
            await _mqttConnector.Subscribe(Config.ReadTopic);
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps.MqttRender"`
Expected: PASS, 0 failed (the existing MqttRender and MqttClockRender tests plus the 2 new ones).

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/api/Apps/MqttRender/MqttRenderApp.cs test/Test/Apps/MqttRender/MqttRenderAppTests.cs test/Test/Apps/MqttRender/MqttClockRenderAppTests.cs
git commit -m "fix(mqtt-render): attach message handler before subscribing

CR-33: a retained message delivered right after SUBACK could arrive
before the handler was attached, leaving the clock blank until the next
publish. MqttClockRenderApp inherits the fix.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 4: `POST /Mqtt/publish` no longer reconnects; 503 on failure (CR-04)

**Files:**
- Create: `test/Test/Controllers/MqttControllerTests.cs`
- Modify (full rewrite): `src/api/Controllers/MqttController.cs`

**Interfaces:**
- Consumes: `IMqttConnector.PublishAsync(string, string) : Task<bool>`, registered in DI by WS1 (`AddAwtrixServices`).
- Produces: `public MqttController(IMqttConnector mqttConnector)`, and `public Task<IActionResult> Publish(string topic, string payload)`, which returns 200 `"Message published"` or 503.

- [ ] **Step 1: Write the failing controller tests**

`test/Test/Controllers/MqttControllerTests.cs`:

```csharp
using AwtrixSharpWeb.Controllers;
using AwtrixSharpWeb.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Test.Controllers
{
    public class MqttControllerTests
    {
        [Fact]
        public async Task Publish_WhenConnectorSucceeds_ReturnsOk_AndOnlyPublishes()
        {
            var connector = new Mock<IMqttConnector>(MockBehavior.Strict);
            connector.Setup(c => c.PublishAsync("awtrix/clock1/notify", "{\"text\":\"hi\"}")).ReturnsAsync(true);
            var controller = new MqttController(connector.Object);

            var result = await controller.Publish("awtrix/clock1/notify", "{\"text\":\"hi\"}");

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("Message published", ok.Value);
            connector.Verify(c => c.PublishAsync("awtrix/clock1/notify", "{\"text\":\"hi\"}"), Times.Once);
            connector.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Publish_WhenConnectorFails_Returns503()
        {
            var connector = new Mock<IMqttConnector>();
            connector.Setup(c => c.PublishAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
            var controller = new MqttController(connector.Object);

            var result = await controller.Publish("awtrix/clock1/notify", "{}");

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(503, objectResult.StatusCode);
        }
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Controllers.MqttControllerTests"`
Expected: build FAILS with `CS1503: Argument 1: cannot convert from 'AwtrixSharpWeb.Interfaces.IMqttConnector' to 'AwtrixSharpWeb.HostedServices.MqttConnector'`.

- [ ] **Step 3: Rewrite the controller (replace the whole file)**

`src/api/Controllers/MqttController.cs`:

```csharp
using AwtrixSharpWeb.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace AwtrixSharpWeb.Controllers
{
    [SwaggerTag("Services")]
    [ApiController]
    [Route("[controller]")]
    public class MqttController : ControllerBase
    {
        private readonly IMqttConnector _mqttConnector;

        public MqttController(IMqttConnector mqttConnector)
        {
            _mqttConnector = mqttConnector;
        }

        /// <summary>
        /// Publish a raw MQTT message. The connector owns the connection; this endpoint never
        /// connects or reconnects (CR-04).
        /// </summary>
        [HttpPost("publish")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Publish(string topic, string payload)
        {
            if (await _mqttConnector.PublishAsync(topic, payload))
            {
                return Ok("Message published");
            }

            return StatusCode(StatusCodes.Status503ServiceUnavailable, "MQTT publish failed: broker not connected or publish rejected");
        }
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Controllers|FullyQualifiedName~Test.CompositionRootTests"`
Expected: PASS, 0 failed. `CompositionRootTests` confirms `IMqttConnector` still resolves to the `MqttConnector` singleton.

- [ ] **Step 5: Run the full suite and confirm there are no leftover reconnect call sites**

Run: `dotnet test`
Expected: 0 failed.

Run: `grep -rn "ConnectAsync()" src/api --include=*.cs`
Expected: no matches.

Run: `grep -rn "new MqttClientFactory" src/api --include=*.cs`
Expected: exactly one match, in `MqttConnector`'s public constructor.

- [ ] **Step 6: Optional manual smoke test (needs a real broker)**

1. Set `AWTRIXSHARP_MQTT__HOST` and run `dotnet run --project src/api`.
2. Restart the broker.
3. Check the log for `Disconnected from MQTT broker`, then `Reconnected to MQTT broker at ... after N attempt(s)`, then `Subscribed to N MQTT topic(s) after connect`.
4. Press a clock button and check that `button clicked` is still logged.
5. Stop the process.

Skip this step if no broker is available. The unit tests cover the logic.

- [ ] **Step 7: Commit**

```bash
git add src/api/Controllers/MqttController.cs test/Test/Controllers/MqttControllerTests.cs
git commit -m "fix(api): /Mqtt/publish no longer reconnects the MQTT client

CR-04: the endpoint called ConnectAsync on every request, which used to
swap in a client with no handlers or subscriptions. It now depends on
IMqttConnector, only publishes, and returns 503 when the publish was
not delivered (200 \"Message published\" unchanged on success).

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

## Self-review (completed by planner)

| Spec item | Task |
|---|---|
| D1 single client, internal ctor, options built once | T1 |
| D2 connector-owned handlers, isolation | T1 |
| D3 subscription registry, resend known topics, replay after connect | T1 (replay on reconnect: T2) |
| D4 serialized, bounded, non-throwing connect | T1 |
| D5 reconnect loop, single-loop guard, backoff, end-of-loop race | T2 |
| D6 honest publish, no reconnect from publish path | T1 |
| D7 stop/dispose | T1 (loop cancellation: T2) |
| D8 IMqttConnector docs, signatures unchanged | T1 |
| D9 CR-33 attach order | T3 |
| D10 CR-04 controller | T4 |
| D11 FakeMqttClient | T1 |
| Acceptance CR-03 §1-12 | T1, T2 (item 12: T4 Step 5 greps) |
| Acceptance CR-04 §1-3 | T4 |
| Acceptance CR-33 §1-3 | T3 |

- **Placeholder scan:** none. Every code step contains complete code.
- **Type consistency:**
  - `FakeMqttClient` members used in T2 (`ConnectOutcomes`, `ConnectCallCount`, `SubscribeRequests`, `SubscribeException`, `Disposed`, `IsConnected`, `SimulateConnectionLostAsync`, `DeliverAsync`) are defined in T1.
  - The internal constructor gains an optional fourth parameter in T2, so T1's three-argument calls still compile.
  - `ReconnectTask` and `GetReconnectDelay` are defined in T2.
- **Pre-verification:** the complete T1 and T2 connector code, `FakeMqttClient`, and the T1/T2 tests were compiled and run against MQTTnet 5.0.1.1416 and Moq 4.20.72 in an isolated scratch project on 2026-09-13.
  - T1 shape: 21 tests passed.
  - T2 shape: 33 tests passed, over 3 consecutive runs.
  - The Moq raise-inside-callback pattern used in T3 was verified there too.
