# WS2 MQTT Connector — Design

- **Date:** 2026-09-13
- **Branch:** `feature/redo`
- **Source:** `code-review.md` → "WS2. MQTT connector: single client, reconnect, subscription registry"
- **Findings:** CR-03, CR-04, CR-33
- **Depends on:** WS1 (`docs/superpowers/specs/2026-09-13-ws1-runtime-resilience-design.md`): the publisher `bool` contract, `IMqttConnector.PublishAsync : Task<bool>`, `MqttPublisher(IMqttConnector, ...)`, and `IMqttConnector` registered in DI
- **Plan:** `docs/superpowers/plans/2026-09-13-ws2-mqtt-connector.md`
- **Status:** Approved for implementation. The owner was unavailable, so the planner made the decisions below and recorded them for review.

---

## 1. Goals

1. **One client for the life of the process.** `MqttConnector` creates its `IMqttClient` once, in the constructor, and reconnects that same instance. There is never a second client, so no TCP connection leaks.
2. **Handlers and subscriptions survive reconnects (CR-03).**
   - `MessageReceived` handlers live in the connector, not on a client.
   - Every subscribed topic goes into a registry, and the registry is replayed after every successful connect.
   - After a broker restart, ButtonApp, MqttRenderApp and MqttClockRenderApp keep receiving messages without a process restart.
3. **Disconnects are detected and repaired in the background (CR-03).**
   - `IMqttClient.DisconnectedAsync` starts one reconnect loop with exponential backoff.
   - A broker that is down at startup is retried the same way.
4. **Honest, non-throwing operations (CR-03).**
   - `PublishAsync` returns `false` while disconnected or on failure, and never triggers a reconnect itself.
   - `Subscribe` never throws; while disconnected it defers the subscription.
   - `StartAsync`, `StopAsync` and `Dispose` never throw.
   - Subscribing, attaching handlers or stopping before a connection exists is safe.
5. **`POST /Mqtt/publish` cannot damage the live connection (CR-04).**
   - The endpoint no longer calls `ConnectAsync`.
   - It returns 503 when the publish was not delivered.
6. **No missed first retained message (CR-33).** `MqttRenderApp` attaches its handler before it subscribes.
7. **Unit-testable without a broker or real time.**
   - The client and the backoff delay are injected through an internal constructor.
   - A hand-rolled `FakeMqttClient` reproduces the MQTTnet 5 behaviours the connector relies on.

## 2. Non-goals

| Not in WS2 | Owner |
|---|---|
| `IAwtrixApp.InitAsync`, removing `Subscribe(...).Wait()` from `ButtonApp.Initialize`, per-app init try/catch, the CR-06 re-init loop | WS3 (CR-05/06) |
| `MqttClockRenderApp` unsubscribing from `SecondChanged`, and the `ScheduledApp` activation state machine | WS4 |
| An `Unsubscribe` API or registry removal. The registry holds only configured topics, so it is bounded. | Not needed (YAGNI) |
| Queueing or replaying publishes that failed while disconnected. Tick-driven apps republish on the next tick; a replayed stale frame is worse than a dropped one. | Not planned |
| Authentication or removal of `/Mqtt/publish` | Deferred (code-review "Deferred") |
| New MQTT settings (port, TLS, client id, keep-alive, backoff) | Not planned: no new config |
| Switching `DiagnosticsController` to `IMqttConnector` | Out of scope; its `PublishAsync` call already gets the new behaviour |

## 3. Constraints

- **Config backward compatibility is mandatory.**
  - `Mqtt:Host`, `Mqtt:Username` and `Mqtt:Password` (and `AWTRIXSHARP_MQTT__HOST/USERNAME/PASSWORD`) keep their exact meaning.
  - Client options stay byte-for-byte equivalent: `WithTcpServer(Host)` (default port 1883), `MqttProtocolVersion.V500`, and credentials only when `Username` is non-empty.
  - MQTTnet defaults stay in place: 15 s keep-alive, clean session, and a random client id generated once per options instance.
- **No new configuration keys, required or optional.** The connect timeout (10 s, as today) and the backoff (1 s doubling, 60 s cap) are code constants.
- **Package:** MQTTnet stays at `5.0.1.1416`. No new packages.
- **Unchanged:** topic strings, HTTP routes and success responses.
  - `POST /Mqtt/publish` still returns `200 "Message published"` on success.
  - It now returns `503` on failure; before, it returned `200` even on failure.
- **Existing seams keep compiling:**
  - The public `MqttConnector(ILogger<MqttConnector>, IOptions<MqttSettings>)` constructor stays. DI and existing tests use it.
  - `IMqttConnector` member signatures do not change; only their documented behaviour does.
- **Target framework:** `net10.0`. All existing tests keep passing, except the ones that documented the old bugs. The plan replaces those explicitly.
- **No real sleeps in tests.** The backoff delay is injected. The only timeouts are `WaitAsync(5 s)` safety nets on tasks that complete immediately.

## 4. Verified MQTTnet 5.0.1.1416 facts (reflection plus a live probe against a closed localhost port, 2026-09-13)

- `IMqttClient : IDisposable` has these members:
  - events `ApplicationMessageReceivedAsync`, `ConnectedAsync`, `ConnectingAsync`, `DisconnectedAsync`, `InspectPacketAsync` (all `Func<TArgs, Task>`)
  - properties `IsConnected` and `Options`
  - methods `ConnectAsync(MqttClientOptions, CancellationToken)`, `DisconnectAsync(MqttClientDisconnectOptions, CancellationToken)`, `PingAsync`, `PublishAsync(MqttApplicationMessage, CancellationToken)`, `SendEnhancedAuthenticationExchangeDataAsync`, `SubscribeAsync(MqttClientSubscribeOptions, CancellationToken)`, `UnsubscribeAsync`
- `MqttClientFactory().CreateMqttClient()` returns `IMqttClient`. There is no managed or auto-reconnecting client.
- **A failed `ConnectAsync` raises `DisconnectedAsync` with `ClientWasConnected == false`**, and then throws `MQTTnet.Exceptions.MqttCommunicationException`.
- `PublishAsync` and `SubscribeAsync` throw `MqttClientNotConnectedException` while disconnected.
- `DisconnectAsync` while not connected completes without throwing.
- `ConnectAsync` after `Dispose` throws `ObjectDisposedException`.
- These event-args and result types have public constructors, so a fake client can build them:
  - `MqttClientDisconnectedEventArgs(bool clientWasConnected, MqttClientConnectResult, MqttClientDisconnectReason, string reasonString, List<MqttUserProperty>, Exception)`
  - `MqttClientConnectedEventArgs(MqttClientConnectResult)`
  - `MqttClientConnectResult()`
  - `MqttClientPublishResult(ushort?, MqttClientPublishReasonCode, string, IReadOnlyCollection<MqttUserProperty>)`
  - `MqttClientSubscribeResult(ushort, IReadOnlyCollection<MqttClientSubscribeResultItem>, string, IReadOnlyCollection<MqttUserProperty>)`
- `MqttClientOptions.ChannelOptions` is `MqttClientTcpOptions`, whose `RemoteEndpoint` renders as `Unspecified/{host}:1883`. `Credentials.GetUserName(options)` and `GetPassword(options)` are available.

## 5. Design decisions

### D1. One client, created in the constructor; an internal test constructor
- `public MqttConnector(ILogger<MqttConnector>, IOptions<MqttSettings>)` chains to `internal MqttConnector(ILogger<MqttConnector>, IOptions<MqttSettings>, IMqttClient client, Func<TimeSpan, CancellationToken, Task>? delay = null)`.
  - Microsoft DI only considers public constructors, so it resolves the public one unambiguously.
  - `InternalsVisibleTo("Test")` already exists.
- `MqttClientOptions` is built once in the constructor, through `internal static BuildClientOptions(MqttSettings)`. Settings come from `IOptions`, not a monitor, so this matches today's per-connect build.
- The connector subscribes once to the client's `ApplicationMessageReceivedAsync` and `DisconnectedAsync`, and unsubscribes in `Dispose`.
- Rejected alternatives:
  - Injecting an `IMqttClientFactory`-style abstraction: an extra type with no benefit.
  - Reflection on `_client`, as today's tests do: brittle, and the field becomes `readonly`.

### D2. The connector owns the handlers and isolates them
- `MessageReceived` has explicit `add`/`remove` accessors over a private multicast delegate, guarded by a lock. They never touch the client, so adding a handler before `StartAsync` is safe; before, it caused a `NullReferenceException`.
- The single client callback takes a snapshot of the delegate. It awaits each handler from `GetInvocationList()` in turn, each in its own try/catch; errors are logged with the handler's `Type.Method` and the topic.
- A throwing app handler therefore cannot stop other apps' handlers, and cannot surface in MQTTnet's receive pipeline.
- Handlers run sequentially, which keeps today's per-message ordering.

### D3. Subscription registry
- `Subscribe(topic)`:
  - Ignores and warns on a null or whitespace topic.
  - Adds the topic to an ordinal `HashSet<string>` under the lock.
  - Sends SUBSCRIBE immediately if connected; otherwise it only logs at Debug.
- **SUBSCRIBE is sent every time `Subscribe` is called while connected, even for a known topic.** MQTT subscribe is idempotent, and re-subscribing makes the broker re-send retained messages. `MqttRenderApp` relies on that at every scheduled activation to show the current value straight away. Skipping known topics would leave the clock blank until the next publish.
- The registry is replayed once, sequentially, **inside `ConnectAsync`, after the client connects**. This happens instead of in a `ConnectedAsync` handler, for two reasons:
  - The connector is the only code that connects the client, so both approaches cover the same events.
  - Replaying after `ConnectAsync` returns is deterministic, and avoids awaiting SUBACKs inside MQTTnet's connect pipeline.
- Race analysis:
  - `Subscribe` adds the topic before it checks `IsConnected`.
  - `ConnectAsync` sets `IsConnected` before it takes the registry snapshot.
  - So every topic is either in the snapshot or subscribed by its own call; at worst it is sent twice, which is harmless.
- A SUBSCRIBE that fails while connected is logged at Warning and not rethrown. The topic stays in the registry and is retried on the next connect.

### D4. Connect: serialized, bounded, never throws
- `public Task<bool> ConnectAsync(CancellationToken = default)` stays public on the concrete type (not on the interface):
  1. Returns `false` after disposal.
  2. Takes a `SemaphoreSlim(1,1)`.
  3. Returns `true` if already connected.
  4. Otherwise connects with a linked token that is cancelled after `ConnectTimeout` (10 s).
- It returns `false` on every exception:
  - timeout → Warning "timed out"
  - caller cancellation → no log
  - anything else → Warning with the message (a routine offline condition, so no stack trace)
- On success it releases the lock, replays subscriptions, and returns `IsConnected`.

### D5. Reconnect loop
- `DisconnectedAsync` handler:
  - Returns immediately if stopping.
  - Logs Warning only when `ClientWasConnected` is true, so failed attempts are not logged twice.
  - Calls `EnsureReconnectLoop()` and returns `Task.CompletedTask`. It never awaits reconnect work inside MQTTnet's event pipeline.
- `StartAsync` makes one connect attempt, bounded by the 10 s timeout as today. If that fails it logs a Warning and calls `EnsureReconnectLoop()`. Host startup is never blocked for longer than today, and a broker down at boot is retried.
- `EnsureReconnectLoop()`:
  - Does nothing if stopping.
  - Uses `Interlocked.CompareExchange` so at most one loop runs.
  - Stores the loop in `internal Task ReconnectTask`, which tests and `StopAsync` await.
  - Because of the verified fact that failed connects raise `DisconnectedAsync`, the guard is essential. The failures during a loop are ignored and do not spawn loops.
- The loop runs `for attempt = 0..` while not stopping and not connected:
  1. `await delay(GetReconnectDelay(attempt), stoppingToken)`
  2. `ConnectAsync(stoppingToken)`
  3. On success, log Information "Reconnected after N attempt(s)"; on failure, log Warning with the next delay.
- **Backoff:** `GetReconnectDelay(attempt) = min(2^min(attempt, 6), 60)` seconds, giving 1, 2, 4, 8, 16, 32, 60, 60 … No jitter: there is a single client per process, so no herd.
- **End-of-loop race:**
  - The loop clears its running flag and then re-checks `!IsConnected && !stopping && !disposed`; if needed it starts a new loop. A disconnect that arrives just as a successful loop finishes is therefore not lost.
  - An unexpected exception, which should not happen because `ConnectAsync` and the delay only throw OCE, is logged at Error and does not restart the loop, so it cannot spin.
- The production delay is `Task.Delay(duration, token)`. Tests inject a recorder that completes immediately, a gate `TaskCompletionSource`, or an infinite delay that honours cancellation.
- `TimeProvider` was not chosen. The delay delegate is the smallest seam and keeps the public constructor unchanged, and `FakeTimeProvider` would need polling for the timer registration.

### D6. Publish
- Payload `null` → empty.
- If disposed or `!IsConnected`, it returns `false` and logs at Debug. It does not touch the client, which avoids an exception per tick while the broker is down.
- Otherwise it builds the message inside a try, awaits `client.PublishAsync`, and returns `false` if the result is non-null and `!IsSuccess`. A `null` result, which some mocks return, counts as success.
- An exception is logged at Warning and returns `false`. **There is no reconnect from the publish path**; the `DisconnectedAsync` loop owns reconnecting. This removes WS1's interim guarded reconnect call.
- **Log volume:** while the broker is down, drops are logged at Debug and loop attempts at Warning, bounded by the backoff. There is no warning per tick. WS1's `AwtrixService` logs the `false` result at Debug.

### D7. Stop and dispose
- `StopAsync`:
  1. Returns if already disposed.
  2. Cancels `_stopping`, so new disconnects and loops are ignored.
  3. Awaits `ReconnectTask.WaitAsync(cancellationToken)`, swallowing OCE.
  4. Calls `DisconnectAsync` if connected, logging any failure at Warning.
  5. Calls `Dispose()`.
- The clean disconnect raises `DisconnectedAsync(true)`, which is ignored because stopping is set.
- `Dispose()` is idempotent (`Interlocked`). It cancels stopping, detaches from the client events and disposes the client.
  - The `SemaphoreSlim` is not disposed. No wait handle is ever allocated, and disposing it would race `Release` in an in-flight connect.
  - The `CancellationTokenSource` has no timer and is left to the GC.
- The DI container also disposes the singleton at host shutdown, which is safe because `Dispose` is idempotent.
- **Hosted-service order is unchanged** (WS1 D9): MqttConnector starts first and stops last, so Conductor's app disposal publishes before the disconnect.

### D8. `IMqttConnector` contract (member signatures unchanged)
- The WS1 interface is kept exactly: `event Func<MqttApplicationMessageReceivedEventArgs, Task> MessageReceived`, `Task Subscribe(string topic)`, `Task<bool> PublishAsync(string topic, string payload)`.
- Only the XML docs change, to state D2/D3/D6: handlers survive reconnects, subscribe records the topic and never throws, publish returns an honest bool and never queues.
- Keeping the signatures avoids churn in the Moq-based tests for ButtonApp, MqttRender, MqttClockRender and Conductor.

### D9. CR-33: `MqttRenderApp` attaches before subscribing
- `ActivateScheduledWork` runs `_mqttConnector.MessageReceived += RawMessageReceived;` first, then `await _mqttConnector.Subscribe(Config.ReadTopic);`. `MqttClockRenderApp` inherits the fix.
- `ButtonApp` is not changed.
  - Its handler already has no client identity to lose (D2).
  - Awtrix button state topics are not the retained-render use case.
  - Reordering it would add a click-on-retained-message behaviour that nobody asked for.
  - With D3, its `Subscribe(...).Wait()` can no longer throw. WS3 removes the `.Wait()`.

### D10. CR-04: `MqttController`
- It depends on `IMqttConnector` (registered by WS1) instead of the concrete `MqttConnector`.
- The `ConnectAsync` call is removed.
- `PublishAsync` returns:
  - `true` → `Ok("Message published")`, as today
  - `false` → `StatusCode(503, "MQTT publish failed: broker not connected or publish rejected")`
- `[ProducesResponseType]` attributes are added for 200 and 503. The route, parameters and success body are unchanged.
- The open-relay concern stays deferred.

### D11. Test doubles
- `test/Test/HostedServices/FakeMqttClient.cs` implements `IMqttClient` and reproduces the §4 behaviours. It supports:
  - `ConnectOutcomes` (a queue of bool)
  - `ConnectCallCount`, `DisconnectCallCount`, `Disposed`
  - `SubscribeRequests`, `Published`
  - `PublishException`, `SubscribeException`
  - `SimulateConnectionLostAsync()`, `DeliverAsync(topic, payload)`
  - `MessageReceivedSubscriberCount`
- It reuses `MqttTestHelpers.CreateReceivedArgs`.
- Moq is still used for `IMqttConnector` in app and controller tests. It is not used for `IMqttClient` in connector tests, because event raising and connection state are clearer in a fake.
- The whole connector, the fake and the connector tests in the plan were compiled and run against MQTTnet 5.0.1.1416 in a scratch project on 2026-09-13: 34 tests passing, 3 consecutive runs. That included the Moq pattern the CR-33 test relies on: `Raise` from inside a `Subscribe` callback with `new object[] { args }`.

## 6. Acceptance criteria

### CR-03: reconnect replaces client; handlers and subscriptions lost; disconnects undetected
1. The connector connects the injected client. `ConnectAsync` while connected does not call the client again. *(MqttConnectorTests)*
2. A handler attached before `StartAsync` receives messages. Removing a handler stops delivery and leaves exactly one client-level subscription. *(MqttConnectorTests)*
3. A throwing handler does not prevent later handlers from running, and no exception reaches the client. *(MqttConnectorTests)*
4. Subscription registry *(MqttConnectorTests)*:
   - `Subscribe` before connect does not throw and is applied on connect.
   - Duplicate pre-connect subscribes are sent once.
   - A known topic re-subscribed while connected is sent again.
   - An empty topic is ignored.
   - A client subscribe exception does not escape.
5. **Broker restart:** after `SimulateConnectionLostAsync`, the same client reconnects (`ConnectCallCount == 2`), both registered topics are re-subscribed, the existing handler receives new messages, and publish returns `true`. *(MqttConnectorReconnectTests)*
6. **Broker down at boot:** `StartAsync` completes. Retries use delays of exactly 1 s, 2 s and 4 s, and connect is called exactly 4 times; a single loop runs despite a `DisconnectedAsync` per failed attempt. Pre-registered topics are subscribed once connected. *(MqttConnectorReconnectTests)*
7. A subscribe that failed while connected is retried on reconnect. *(MqttConnectorReconnectTests)*
8. `PublishAsync` *(MqttConnectorTests, MqttConnectorReconnectTests, MqttPublisherConnectorTests)*:
   - Returns `false` while disconnected without touching the client, and `true` again after reconnect.
   - Returns `false` without reconnecting when the client throws.
   - Sends the exact topic and UTF-8 payload; `null` becomes an empty payload.
9. `StopAsync` *(MqttConnectorTests, MqttConnectorReconnectTests)*:
   - Before start: does not throw and disposes the client.
   - When connected: disconnects once and disposes.
   - During backoff: completes, cancels the loop, makes no further connect attempt.
   - A clean stop does not start a reconnect loop.
   - After stop: publish, subscribe and connect return without throwing.
10. `GetReconnectDelay` gives 1, 2, 4, 32, 60 and 60 s for attempts 0, 1, 2, 5, 6 and 50. *(MqttConnectorReconnectTests)*
11. Client options keep the configured host (port 1883), MQTT 5.0 and credentials only when a username is set. *(MqttConnectorTests)*
12. Code review: no `ConnectAsync` call in the publish path, no `new MqttClientFactory()` outside the public constructor, and `_client` is `readonly`.

### CR-04: `POST /Mqtt/publish` destroys the live client
1. A successful publish returns `OkObjectResult` with `"Message published"`, and the connector sees exactly one `PublishAsync(topic, payload)` and no other calls. *(MqttControllerTests)*
2. A failed publish returns `ObjectResult` with `StatusCode == 503`. *(MqttControllerTests)*
3. `MqttController` has no dependency on the concrete `MqttConnector`, which a compile-time check confirms: its constructor takes `IMqttConnector`.

### CR-33: MqttRenderApp attaches its handler after subscribing
1. A message delivered synchronously from inside `Subscribe` (a simulated retained message) is rendered through `AppUpdate`. *(MqttRenderAppTests)*
2. The same holds for `MqttClockRenderApp` through inheritance. *(MqttClockRenderAppTests)*
3. The existing MqttRender tests keep passing.

## 7. Testing strategy

- **TDD per task:**
  1. Write the failing tests.
  2. Run them filtered and confirm they fail (a compile error or assertion counts).
  3. Implement.
  4. Run them filtered and confirm they pass.
  5. Run the full `dotnet test` with 0 failed, then commit.
- **Connector:** use `FakeMqttClient` through the internal constructor. There is no reflection, no broker and no socket.
- **Reconnect timing:** inject the delay delegate:
  - a recorder that completes immediately, for backoff sequence assertions
  - a `TaskCompletionSource` gate, to observe the disconnected window
  - `Task.Delay(Timeout.InfiniteTimeSpan, token)`, for stop-during-backoff
  - Background work is awaited through `connector.ReconnectTask.WaitAsync(5 s)`. The 5 s is a failure-mode guard, not a sleep.
- **Replaced tests:**
  - `MqttConnectorTests` is rewritten. Its reflection-based tests documented the old client-swapping behaviour and the before-connect `ArgumentNullException`s, which are exactly the bugs being fixed.
  - `MqttPublisherConnectorTests` switches from reflection to the internal constructor with a connected `FakeMqttClient`, and gains a disconnected → `false` case.
- **Apps and controller:** Moq `IMqttConnector`, following the existing test style.
- **Manual smoke:** optional. Run `dotnet run --project src/api` against a real broker, restart the broker, and check the log for "Disconnected …; reconnecting", "Reconnected … after N attempt(s)" and "Subscribed to N MQTT topic(s) after connect".

## 8. Risks

- **WS1 drift.** WS2 is written against the WS1 plan's code. Plan Task 0 checks the assumed seams and adapts names if WS1 landed differently.
- **Concurrent edits.** Another agent may still be committing WS1 in the same working tree. WS2 must start only after WS1's final commit. WS3 also edits `ButtonApp`/`Conductor`, and WS2 avoids both.
- **Duplicate SUBSCRIBE on the connect/subscribe race (D3)** can make a retained message render twice. This is harmless.
- **Messages published while disconnected are dropped**, and the result says so honestly. Displays catch up on the next tick or publish; a queue is a non-goal.
- **The same client id on every reconnect** (it is generated once per options instance) is fine with clean start. Two AwtrixSharp instances still get different ids, as today.
- **The real MQTTnet event ordering** during `ConnectAsync` is not unit-tested beyond the verified facts in §4. The optional manual smoke covers it.
