# WS3 Conductor and Hosted-Service Lifecycle — Design

- **Date:** 2026-09-13
- **Branch:** `feature/redo`
- **Source:** `code-review.md` → "WS3. Conductor and hosted-service lifecycle"
- **Findings:** CR-05, CR-06, CR-07, CR-16, CR-30
- **Depends on:**
  - WS1 (`docs/superpowers/specs/2026-09-13-ws1-runtime-resilience-design.md`): publishers and `AwtrixService` never throw; interface-only `Conductor` constructor; `ConductorTestHelper`; `AwtrixApp.FireAndLog`; no `ButtonApp` for HTTP devices.
  - WS2 (`docs/superpowers/specs/2026-09-13-ws2-mqtt-connector-design.md`): `IMqttConnector.Subscribe` records the topic synchronously, never throws, and returns at once while disconnected.
- **Written against:** the code shape after WS2. WS2 does not touch `Conductor.cs`, `ButtonApp.cs`, `AwtrixApp.cs` or `Program.cs`, so today's versions of those files are the baseline.
- **Plan:** `docs/superpowers/plans/2026-09-13-ws3-conductor-lifecycle.md`
- **Status:** Approved for implementation. The owner was unavailable, so the planner made the decisions below and recorded them for review.

---

## 1. Goals

1. **A broken app or an unreachable broker/device never stops the host starting (CR-05).**
   - App init is async (`IAwtrixApp.InitAsync`). There is no `.Result` or `.Wait()` on the startup path.
   - Each app's creation and init runs in its own try/catch. A failure is logged with device, app type and reason, and the other apps still start.
   - `ButtonApp` no longer blocks on `Subscribe(...).Wait()`.
2. **Every app is initialised exactly once (CR-06).** All apps for all devices are created first, then each is initialised once. `AwtrixApp.InitAsync` also guards itself against a second call.
3. **Unknown or incomplete app config is skipped with a useful message (CR-30).**
   - An unknown `Type` logs `Unknown app type '{Type}' on device '{Device}'` plus the list of known types.
   - A missing `Type` logs a warning.
   - A missing `CronSchedule` fails that app's init and is logged and skipped.
4. **`ExecuteNow` drives the running instance (CR-07).**
   - A registry keyed by (device `BaseTopic`, app `Type`) replaces the anonymous list.
   - `ExecuteNow` calls the registered instance and returns `AppExecutionResult { NotFound, Started, Error }`.
   - `TripTimerController` and `MqttRenderController` map that result to 404/200/500.
   - No transient, cron-armed duplicate app is ever created.
5. **Shutdown is bounded and never throws.**
   - `Conductor.StopAsync` awaits each app's `DisposeAsync` in its own try/catch, bounded by a per-app timeout and the host's shutdown token.
   - `SlackConnector.StopAsync` no longer throws a `NullReferenceException` when Slack was never configured (CR-16).
6. **The TripTimer right-double-click binding keeps working, per device**, and fires once per double-click.
7. **A written lifecycle contract** that WS4 (ScheduledApp engine, dispose pattern), WS5 and WS7 (CR-23 validation) build on (§5).

## 2. Non-goals

| Not in WS3 | Owner |
|---|---|
| ScheduledApp CTS state machine: re-arming after dispose, `ExecuteNow` during an active run, ActiveTime for manual runs (CR-08, CR-18) | WS4 |
| A single virtual dispose pattern across `AwtrixApp`/`ScheduledApp`/`TripTimerApp`, unsubscribing in Dispose, `Dismiss` removing other apps' notifications (CR-31). WS3 adds only the `DisposeAsync` seam (D8). | WS4 |
| `MqttClockRenderApp` `SecondChanged` leak (CR-09) | WS4 |
| Eager typed-config validation (CR-23). WS3 provides the per-app guard it plugs into. | WS7 |
| `SlackConnector` static fields, dead members, env-var-only token (CR-41, CR-14) | WS8 / WS7 |
| Distinct app `Name` for two apps of the same Type on one clock | Deferred (owner decision) |
| An `IConductor` interface for controllers | Not needed: controller tests use a real `Conductor` built by `ConductorTestHelper` |
| Retrying a failed app init | Not planned: the fix is a config change and a restart (D4) |

## 3. Constraints

- **Config backward compatibility is mandatory.**
  - No `appsettings.json` keys or `AWTRIXSHARP_*` / `TRANSPORTOPENDATA__APIKEY` env vars are added, renamed or reinterpreted.
  - `Awtrix:Devices[].BaseTopic`, `Apps[].Type`, `Apps[].Config` and `ValueMaps` keep their meaning.
- **No new configuration, required or optional.** The per-app dispose timeout (10 s) is a code constant.
- **Unchanged HTTP routes and query parameters.**
  - `POST api/app/TripTimer/start?baseTopic&appName` and `POST api/app/MqttRender/start?deviceAddress&appName` stay as they are.
  - Success is still 200.
  - New: 404 when the app is not running on that device; 500 when the app threw.
  - `TripTimer/start` used to return an empty 200 body. It now returns `{ message }`, the same shape as `MqttRender/start`.
- **Unchanged MQTT topics and HTTP device URLs.** Hosted-service registration order is unchanged: MqttConnector, SlackConnector, Conductor, TimerService. Conductor still stops before MqttConnector.
- **Target framework `net10.0`, and no package changes.**
- **Tests:**
  - All existing tests keep passing, except those whose subject is removed. The plan replaces those explicitly:
    - the `AppFactory_UnknownType_Throws…` test
    - the reflection on `Conductor._apps`
    - `TestTimingConfig_NoTripTimerAppRegistered_Throws`
    - the `MqttRenderController` "Ok for unknown device" tests
  - No real sleeps. `WaitAsync(5 s)` is used only as a failure-mode guard.

## 4. Design decisions

### D1. `IAwtrixApp.InitAsync()` replaces `Init()`
- The signature is `Task InitAsync()`. `AwtrixApp.InitAsync` awaits `AppClear()`, logs, then calls the existing `protected abstract void Initialize()`.
- `Initialize` stays synchronous. No app needs async wiring once `ButtonApp` stops blocking (D2), and keeping it synchronous avoids churn in WS4/WS5 files. WS4 may add an async hook later.
- Call sites:
  - `Conductor.StartAsync`: awaited.
  - `Conductor.ExecuteNow`: removed (D6).
  - Tests: `AwtrixAppTests`, `ButtonAppTests`, `DiurnalAppTests`, `ScheduledAppTests`, `SlackStatusAppTests`.
- **Test call sites use a mechanical rename:** `.Init()` → `.InitAsync().GetAwaiter().GetResult()`. The rewritten `AwtrixAppTests`/`ButtonAppTests` tests use `await`.
  - This is safe because Moq returns completed tasks, so `InitAsync` completes synchronously in those tests.
  - It keeps a 20-call-site change mechanical and reviewable.
- `TripTimerController` never called `Init`. The memory note about an earlier rename refers to another branch; HEAD still has `void Init()`.

### D2. `ButtonApp` subscribes without blocking
- `Initialize` issues each of the three `Subscribe(topic)` calls through `_ = FireAndLog(() => _mqttConnector.Subscribe(topic), …)`, then attaches its forwarders and `MessageReceived` handler as today.
- Why not `await`:
  - WS2's `Subscribe` adds the topic to the registry before its first await, so the subscription is durable, and it is replayed on reconnect, as soon as the call starts.
  - While connected, it awaits the SUBACK with no timeout of its own, and MQTTnet's default communication timeout is long. Awaiting could still stall startup on a half-open connection.
  - `FireAndLog` observes the task anyway.
- The subscribe-then-attach order is unchanged (WS2 D9 declined to reorder ButtonApp).

### D3. Startup in three phases
`Conductor.StartAsync`:

1. **Create.** For each device in config order:
   - Skip a device with a null or blank `BaseTopic`, with a warning. It cannot be a registry key.
   - Create a `ButtonApp` for MQTT devices (no change from WS1 D8).
   - Create each configured app.
   - Creation is guarded per app (D5). Nothing is initialised in this phase.
2. **Init.** `await Task.WhenAll(created.Select(InitOneAsync))`.
   - Each `InitOneAsync` awaits `InitAsync` in its own try/catch.
   - Inits run concurrently across apps. Apps share no mutable state during init, and a device that is offline over HTTP otherwise costs 5 s per app, one after another.
   - With a disconnected MQTT broker every publish returns `false` immediately, so the effect is the same either way.
3. **Bind and register.**
   - For each device, bind among its *successfully initialised* apps only: Click/DoubleClick logging, and Right double-click → `tripTimerApp.ExecuteNow()`, wrapped in try/catch.
   - Then add the running apps to the registry and log "started N of M app(s)".

- **Grouping:** apps are grouped by their `DeviceConfig` instance (reference equality), not by the `BaseTopic` string. Two config entries with the same `BaseTopic` therefore do not cross-bind buttons.
- **Second `StartAsync` call:** it logs a warning and does nothing (`Interlocked` guard).
- **Not using `cancellationToken`:** `StartAsync` does not observe it. Every await is bounded by publisher timeouts, and an aborted startup is stopped by the host anyway.

### D4. Init exactly once
- `AwtrixApp.InitAsync` has an `Interlocked.Exchange(ref _initState, 1)` guard. A second call logs a warning and returns without calling `AppClear` or `Initialize`.
- The guard is set before the work. **An init that threw is not retried.** Retrying `Initialize` could double-subscribe events that were attached before the throw; the fix for a bad config is a config change and a restart.
- Conductor structure (D3) is the real fix. The guard is defence in depth for WS4/WS5 code and for tests.

### D5. Per-app isolation and unknown types
- **Creation:**
  - `AddIfCreated(device, appConfig)` skips a null config or a blank `Type` with a warning.
  - It calls `AppFactory` inside try/catch. `appConfig.As<T>()`, constructors, and WS7's future `Validate()` calls can all throw.
  - A thrown exception is logged at Error: `Failed to create app {AppType} on device {Device}: {Reason}; skipping`.
- **`AppFactory`** returns `IAwtrixApp?`. The `default` branch logs Warning `Unknown app type '{AppType}' on device '{Device}'; skipping. Known types: {KnownTypes}` (from `AppNames.All`) and returns `null`. There is no more `NotImplementedException`.
- **Init:** a throw is logged at Error (`Failed to initialise app {AppType} on device {Device}: {Reason}; the app will not run`, with the exception). The app is then **disposed** best-effort (D8) and **not registered**:
  - Disposing releases whatever it attached before the throw. Today disposal only clears the slot and cancels a CTS; after WS4 it also unsubscribes.
  - Not registering means `ExecuteNow` returns NotFound, `FindApps` does not return it, and `StopAsync` does not dispose it twice.
- **Logger category:** `ButtonApp` gets a `ButtonApp` logger category instead of `MqttRenderApp` (CR-42, fixed in passing because the factory is being rewritten).

### D6. Registry and `ExecuteNow`
- **Storage:** `private sealed record RegisteredApp(AwtrixAddress? Device, string Type, IAwtrixApp App)`, with `BaseTopic => Device?.BaseTopic ?? ""`, held in a `List` behind a lock. Controllers read it concurrently with `StopAsync`.
- **Key:** (`BaseTopic`, `Type`), both compared with `StringComparison.Ordinal`.
  - This matches today's `==` lookups. MQTT topics are case-sensitive, and the factory switch on `Type` is case-sensitive.
  - Case-insensitive matching belongs with WS7's CR-22.
- **Duplicates:** the same (`BaseTopic`, `Type`) pair is allowed, as today, because both apps run. `ExecuteNow` runs every match.
- **`FindApps(string appType, string? baseTopic = null)`** returns a copy. `null` means all devices. The existing one-argument calls still compile.
- **`internal void RegisterApp(IAwtrixApp app)`** is a test seam. It keys the app by `app.AwtrixAddress` and `app.GetConfig()?.Type`, and replaces the reflection on `_apps` in tests.
- **`public AppExecutionResult ExecuteNow(string baseTopic, string appType)`:**
  - A blank argument or no match → `NotFound`, logged at Warning.
  - Otherwise it calls `App.ExecuteNow()` on each match in its own try/catch:
    - every call succeeds → `Started`
    - any call throws → `Error`, logged with the exception
  - It never creates, initialises or disposes an app, and it never throws.
- **`AppExecutionResult`** is a public enum in `AwtrixSharpWeb.HostedServices`.
- **Controllers** share `AppExecutionResponses.ToActionResult(this ControllerBase, result, appName, baseTopic)`:
  - `Started` → `Ok(new { message = "App '{app}' started on device '{device}'" })`
  - `NotFound` → `NotFound(new { message = "App '{app}' is not running on device '{device}'" })`
  - `Error` → `StatusCode(500, new { message = "App '{app}' failed to start on device '{device}'; see the service log" })`
- **`TripTimerController.TestTimingConfig`** returns 404 instead of throwing `InvalidOperationException` when no `TripTimerApp` is running. It is the same controller, and it has the same registry-miss shape.
- **Behavioural note:** `ExecuteNow` on a `ScheduledApp` still starts a run with no ActiveTime limit, and a second call during a run still restarts it. Both are CR-08/CR-18, owned by WS4. WS3 only stops the duplicate instances.

### D7. `StopAsync` clears the registry first
- `StopAsync` takes a snapshot of the registry and clears it under the lock, then disposes the snapshot. `ExecuteNow` or a double-click that arrives during or after shutdown finds nothing, so a disposed app is never executed.
- The button-to-TripTimer closure can still fire on a disposed TripTimerApp during shutdown, because the `ButtonApp` handler is still attached until the ButtonApp is disposed. The call is wrapped in try/catch; WS4's `_disposed` flag makes it a no-op.

### D8. Async disposal seam and bounded shutdown
- **The seam:** `IAwtrixApp : IDisposable, IAsyncDisposable`.
  - `AwtrixApp`: `public virtual async ValueTask DisposeAsync() => await AppClear();`. This is today's `Dispose()` without `.Wait()`.
  - `ScheduledApp` overrides it: log, cancel and dispose `_cts` (ending an active run, whose `finally` deactivates), then `await Dismiss()`, then `await AppClear()`. This is today's `Dispose(true)`, with the fire-and-forget clears awaited so they reach MQTT before the connector disconnects.
  - `TripTimerApp` is unchanged; its deactivation runs through the cancelled run.
  - Synchronous `Dispose()` stays as it is for existing callers and tests. **Conductor never calls it.** WS4 unifies the two.
- **`Conductor.DisposeOneAsync(entry, token)`:**
  - `await app.DisposeAsync().AsTask().WaitAsync(AppDisposeTimeout, token)` inside try/catch:
    - `TimeoutException` → Warning
    - OCE while the token is cancelled → Warning
    - anything else → Error
  - It never throws. A dispose that times out keeps running in the background; it is bounded by publisher timeouts.
- **`AppDisposeTimeout = 10 s`** (internal constant): enough for two sequential 5 s HTTP publishes (Dismiss + AppClear) to an offline device.
- **`StopAsync`** disposes all apps concurrently with `Task.WhenAll`, so N offline devices cost about 10 s, not N × 10 s. That stays under the .NET host's default 30 s `ShutdownTimeout`.

### D9. `SlackConnector.StopAsync` (CR-16)
- `_slackSocketClient?.Disconnect()`.
- Cancel/Disconnect gain a `catch (Exception)` that logs a Warning, so a SlackNet failure cannot fail host shutdown.
- Static fields and dead members are left to WS8 (CR-41).

## 5. App lifecycle contract (for WS4, WS5, WS7)

| Stage | Guarantee from Conductor | Obligation on the app |
|---|---|---|
| Construct | Called once per config entry, in the Create phase. A throw skips only this app. | Must not publish, subscribe or start timers. Store dependencies only. |
| `InitAsync` | Called **at most once**, after **every** app for **every** device is constructed. May run concurrently with other apps' `InitAsync`. A throw means the app is disposed and never registered. | Clear the slot and wire subscriptions/schedules. Must not block beyond publisher timeouts; `AwtrixApp` guards re-entry. WS7 validation may throw here or in the constructor. |
| Running | The app is in the registry. `ExecuteNow` may be called 0..n times from controller threads or the MQTT receive thread (button). `ExecuteNow` throwing yields a 500 / logged Error. | `ExecuteNow` must be thread-safe against itself and against cron wake-ups (WS4 CR-08). |
| `DisposeAsync` | Called **exactly once** for every constructed app whose init was attempted: at `StopAsync` for running apps, immediately for init failures. It runs before `MqttConnector` stops, concurrently with other apps, and is abandoned after 10 s or when the host token is cancelled. Throws are logged. | Cancel work, unsubscribe (WS4), await final clears. Must tolerate being called after a failed `InitAsync`. Sync `Dispose` is not called by Conductor. |

- **Shutdown order:** TimerService stops first (no more ticks), then Conductor (apps disposed), then SlackConnector, then MqttConnector (disconnect last). This is unchanged from WS1 D9.

## 6. Acceptance criteria

### CR-05: slow start, host crash, one bad app kills all
1. `IAwtrixApp` exposes `Task InitAsync()` and no `Init()`. No `.Result`/`.Wait()` remains in `AwtrixApp.InitAsync`, `ButtonApp.Initialize` or `Conductor.StartAsync` (code review, grep).
2. `ButtonApp.InitAsync` completes while `IMqttConnector.Subscribe` never completes, and a later button message still raises `Click`. *(ButtonAppTests)*
3. `Conductor.StartAsync` completes (within the 5 s guard) when every `Subscribe` call never completes. *(ConductorStartupTests)*
4. When one app's init throws, `StartAsync` does not throw, the failing app is not in `FindApps`, and another app on the same device is. *(ConductorStartupTests)*
5. The existing unreachable-HTTP-device startup test still passes. *(ConductorTests)*

### CR-06: re-initialisation per later device
1. With two MQTT devices, each with a DiurnalApp:
   - `AppClear(clock1, "DiurnalApp")` and `AppClear(clock1, "ButtonApp")` are each called once.
   - `Subscribe("awtrix/clock1/stats/buttonLeft")` is called once.
   - `MinuteChanged` is subscribed exactly twice.
   *(ConductorStartupTests)*
2. `InitAsync` called twice clears and initialises once; a first attempt that threw is not retried. *(AwtrixAppTests)*
3. A second `StartAsync` does not create or initialise anything. *(ConductorStartupTests)*
4. A right double-click on clock1 starts clock1's TripTimerApp exactly once (one `Notify(clock1, …)`) and never clock2's. *(ConductorStartupTests)*

### CR-07: ExecuteNow leaks a cron-armed duplicate
1. `ExecuteNow` on a registered app calls that instance's `ExecuteNow` once per call, returns `Started`, and leaves `FindApps` unchanged. *(ConductorExecuteNowTests)*
2. After `StartAsync` with a DiurnalApp, `ExecuteNow(device, "DiurnalApp")` does not call `AppClear` again (no new instance initialised). *(ConductorExecuteNowTests)*
3. `ExecuteNow` returns `NotFound`, without throwing, for:
   - an unknown device
   - an unknown type
   - a blank base topic

   It targets only the requested device. *(ConductorExecuteNowTests)*
4. A throwing app yields `Error`. With two duplicates where one throws, both are invoked. *(ConductorExecuteNowTests)*
5. Controllers *(MqttRenderControllerTests, TripTimerControllerTests)*:
   - `MqttRenderController.StartNow` and `TripTimerController.StartNow`: `Started` → `OkObjectResult` naming app and device; `NotFound` → `NotFoundObjectResult`; `Error` → `ObjectResult` with `StatusCode == 500`.
   - `TestTimingConfig` with no TripTimerApp → `NotFoundObjectResult`.

### CR-16: SlackConnector.StopAsync NRE
1. `StopAsync` with an executing task but no socket client does not throw. *(SlackConnectorTests)*
2. `StopAsync` before `StartAsync` does not throw. *(SlackConnectorTests)*

### CR-30: unknown Type / missing CronSchedule
1. `AppFactory` returns `null` (does not throw) for an unknown type. *(ConductorTests)*
2. `StartAsync` does not throw when a device lists `"MqttRendrApp"`, an entry without `Type`, or a `MqttRenderApp` without `CronSchedule`. In each case the valid DiurnalApp on the same device runs. *(ConductorStartupTests)*
3. The init-failed `MqttRenderApp` is disposed: `Dismiss(device)` is called once. *(ConductorStartupTests)*

### Shutdown (WS3 approach item; completes the WS1 D9 interim)
1. `StopAsync` calls `DisposeAsync` once on every registered app and never calls `Dispose`. *(ConductorShutdownTests)*
2. `StopAsync` does not complete until a pending `DisposeAsync` completes. *(ConductorShutdownTests)*
3. A throwing `DisposeAsync` does not stop other apps being disposed, and `StopAsync` does not throw. *(ConductorShutdownTests)*
4. With a cancelled shutdown token, `StopAsync` completes despite an app whose `DisposeAsync` never completes. *(ConductorShutdownTests)*
5. After `StopAsync`, `FindApps` is empty and `ExecuteNow` returns `NotFound`. *(ConductorShutdownTests)*
6. `AwtrixApp.DisposeAsync` awaits `AppClear` without blocking. `ScheduledApp.DisposeAsync` ends an active run and awaits `Dismiss`. *(AwtrixAppTests, ScheduledAppTests)*

## 7. Testing strategy

- **TDD per task:**
  1. Write the failing tests.
  2. Run them filtered; they fail (a compile error counts).
  3. Implement.
  4. Run them filtered; they pass.
  5. Run the full `dotnet test` with 0 failed, then commit.
- **Conductor:** use `ConductorTestHelper.Create(...)` over Moq interfaces. It gains optional `timerService`, `tripPlanner` and `slackConnector` parameters, and a `MockApp(baseTopic, type)` helper.
  - Startup tests use real app types built by the factory; registry, ExecuteNow and shutdown tests use `RegisterApp` with mocks.
  - No reflection on Conductor internals except the existing `AppFactory` factory tests.
- **Moq ordering:** Moq returns completed tasks, so startup, init and the button → TripTimer → `Notify` chain run synchronously and can be verified right after the call.
  - Blocking scenarios run the call on `Task.Run(...)` with `WaitAsync(5 s)`, so a regression fails rather than hangs.
  - Continuations that could be posted to xUnit's synchronization context are awaited through a `TaskCompletionSource` (`ScheduledAppTests`).
- **SlackConnector:** reflection sets `_executingTask` to a completed task and `_slackSocketClient` to null. That reproduces "started without a token" deterministically, whatever the developer's environment variables are.
- **No manual smoke test** that runs the app (it would reach a real broker and clock).

## 8. Risks

- **WS1/WS2 drift.** WS3 assumes WS2's non-throwing, registry-first `Subscribe` and WS1's `ConductorTestHelper` shape. Plan Task 0 checks both and adapts names.
- **Concurrent edits.** Do not start while WS2 commits are in progress (clean `git status`). WS4 and WS5 edit `ScheduledApp`, `AwtrixApp`, `DiurnalApp` and `SlackStatusApp` after WS3; the lifecycle contract (§5) is the hand-off.
- **Dispose re-arms a cron wait (existing CR-08).**
  - Cancelling a `ScheduledApp` run inside `DisposeAsync` runs `WakeUp`'s `finally`, which calls `ScheduleNextWakeUp()`, so a new background cron wait is armed after dispose.
  - At process shutdown this is harmless. For init-failed apps it cannot happen: `Initialize` threw before scheduling.
  - WS4's `_disposed` flag fixes it.
- **Concurrent init** interleaves startup log lines across apps. Each line carries the device and app type.
- **Docker's default 10 s stop grace period** is shorter than the host's 30 s. With an offline HTTP device, final clears may be cut off by SIGKILL; the display impact is the same as today.
- **Behaviour change visible to API callers:** `start` endpoints now return 404 for a device or app that is not running, instead of a misleading 200. This is intended by CR-07 and listed in §3.
- **Duplicate (`BaseTopic`, `Type`) entries** still both run and share a display slot (the deferred distinct-`Name` item). `ExecuteNow` triggers both.
