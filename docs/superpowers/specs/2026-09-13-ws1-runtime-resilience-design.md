# WS1 Runtime Resilience — Design

- **Date:** 2026-09-13
- **Branch:** `feature/redo`
- **Source:** `code-review.md` → "WS1. Runtime resilience: timer, publish path, test seams"
- **Findings:** CR-01, CR-02, CR-11, CR-12, CR-35
- **Plan:** `docs/superpowers/plans/2026-09-13-ws1-runtime-resilience.md`
- **Status:** Approved for implementation (owner unavailable; decisions below were taken by the planner and are recorded so they can be reviewed)

---

## 1. Goals

WS1 sets up two runtime contracts that WS2-WS5 rely on, plus the test seams needed to check them.

1. **A clock tick can never take down the process.** A subscriber to `ITimerService.SecondChanged` / `MinuteChanged` that throws (synchronously or asynchronously) is logged. The process keeps running, the other subscribers still run, and the timer keeps ticking.
2. **Publishers never throw and return an honest `bool`.** `AwtrixPublisher.Publish(...)` returns `true` only when the transport confirmed the hand-off (HTTP 2xx, or MQTT client `PublishAsync` completed). Every failure is logged and returned as `false`. `AwtrixService` also never throws.
3. **Ticks do not overlap or duplicate.** One sequential loop drives ticks. At most one `SecondChanged` fires per wall-clock second, and `MinuteChanged` fires whenever the minute changes, including after a stall.
4. **HTTP devices get correct custom-app URLs.** For HTTP, `AppUpdate`/`AppClear` target `POST {base}/custom?name={app}`. MQTT topics do not change.
5. **Test seams.** Time comes from `TimeProvider`/`IClock`. `Conductor`, `MqttPublisher` and `SlackStatusApp` depend on interfaces, and the DI graph is checked by a test. `Conductor.StartAsync` can be unit-tested without a broker or network.

## 2. Non-goals (owned by later workstreams)

| Not in WS1 | Owner |
|---|---|
| Rebuilding `MqttConnector` (single client, reconnect loop, subscription registry). WS1 changes only `PublishAsync`'s return type and guards its reconnect call. | WS2 (CR-03/04/33) |
| `IAwtrixApp.InitAsync` rename; removing `AppClear().Result` in `AwtrixApp.Init`; per-app try/catch in `StartAsync`; the CR-06 re-init loop; unknown `Type` handling; `ExecuteNow` registry | WS3 (CR-05/06/07/30) |
| `ScheduledApp` CTS state machine, the Dispose pattern, `MqttClockRenderApp` unsubscribe, `TripTimerApp` `{}`/`_cts` redesign. WS1 only stops these from crashing the tick thread. | WS4 (CR-08/09/19/31) |
| Diurnal catch-up ("apply all entries in (last, now]"), eager validation, Slack handler null-safety, `DoubleClickDetector` monotonic time, ValueMap fixes | WS5 (CR-20/21/24/32/34) |
| Trip planner HttpClient lifetime, timeout and resilience | WS6 (CR-29) |
| Config precedence, secrets via `IConfiguration` | WS7 |
| Rate-limiting repeated "device offline" warnings | Deferred (see Risks) |

## 3. Constraints

- **Config backward compatibility is mandatory.** No `appsettings.json` keys or `AWTRIXSHARP_*` / `TRANSPORTOPENDATA__APIKEY` env vars are added, renamed or reinterpreted. `BaseTopic` keeps its meaning: an MQTT topic prefix, or `http(s)://<ip>/api` for HTTP devices.
- **No new required configuration.** The new values (5 s HTTP timeout, 100 ms tick interval) are code constants.
- MQTT topic strings stay byte-for-byte identical for every operation (`/notify`, `/notify/dismiss`, `/settings`, `/rtttl`, `/custom/{name}`, `/stats/button*`).
- HTTP URLs stay identical except custom apps (CR-12).
- Target framework `net10.0`. The only new package is `Microsoft.Extensions.TimeProvider.Testing` 10.0.0, in `test/Test` only. `TimeProvider`, `PeriodicTimer(TimeSpan, TimeProvider)` and `IHttpClientFactory` already ship with the BCL / ASP.NET Core shared framework.
- All existing tests keep passing. Baseline on 2026-09-13: `test/Test` has 168 passing and 2 skipped. Tests whose subject is removed are replaced explicitly in the plan:
  - `TimerServiceEventTests` reflection tests on `_lastTime` / `CheckTimeChange`
  - `AwtrixSettingsTests.ToString_WhenEmpty_ThrowsInvalidOperationException`
- Public HTTP API routes and response codes do not change.

## 4. Design decisions

### D1. `TimeProvider` for the timer; `IClock` stays for apps and is backed by `TimeProvider`
- `TimerService` takes `TimeProvider? timeProvider = null` (default `TimeProvider.System`) and drives a `PeriodicTimer(TickInterval, timeProvider)`. Tests use `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`. It fits exactly: it controls both "now" and timer firing, and it supports `SetLocalTimeZone`.
- `IClock` (`DateTimeOffset Now`) is already used by `ScheduledApp`, `TripTimerApp` and three test files via `MockClock`. Replacing it would add churn with no WS1 benefit. `Clock` gains `Clock(TimeProvider? timeProvider = null)` with `Now => timeProvider.GetLocalNow()`, so production has one time source.
- `DiurnalApp` gets `IClock` in place of `DateTime.Now` (CR-35). WS4/WS5 may later migrate apps to `TimeProvider` directly.
- DI registers `TimeProvider.System` as a singleton and `IClock` → `Clock` as a singleton.

### D2. Tick semantics
- `ClockTickEventArgs.Time` is **local wall-clock time, truncated to the whole second, `DateTimeKind.Local`**. This is the documented contract.
- Why `Kind` matters: `TimeProvider.GetLocalNow().DateTime` has `Kind=Unspecified`, and `DateTime.ToLocalTime()` on an Unspecified value treats it as UTC. That would shift Diurnal by the UTC offset. `DiurnalApp` therefore stops calling `ToLocalTime()` and uses `e.Time.TimeOfDay`.
- Detection compares whole truncated `DateTime` values, not `.Second`/`.Minute` fields:
  - `SecondChanged` fires when `currentSecond != lastSecond`.
  - `MinuteChanged` fires when `Truncate(current, minute) != Truncate(last, minute)`.
  - `_lastSecond` is updated **before** handlers run.
  - A stall of exactly N minutes still fires both events.
- **Coalescing:** after a stall, one `SecondChanged` (latest time) and at most one `MinuteChanged` fire. Skipped seconds and minutes are not replayed. Diurnal catch-up is WS5 (CR-20).
- A backwards clock change (DST end, NTP step) counts as a change and fires events. Diurnal may re-apply an hour's settings; that is harmless.

### D3. A non-overlapping loop plus per-subscriber isolation
- `ExecuteAsync` is `while (await timer.WaitForNextTickAsync(ct)) Tick();`. One loop means no concurrent callbacks. If handlers block, `PeriodicTimer` keeps at most one pending tick, so ticks do not pile up.
- `internal void Tick()` is the unit under test (`InternalsVisibleTo("Test")` already exists). It wraps its body in try/catch and calls each delegate from `GetInvocationList()` in its own try/catch. The log names the event and `DeclaringType.Method`.
- Rejected: a `Channel` per app. It adds lifecycle and backpressure design that WS4 owns. Also rejected: an `Interlocked` guard on the old `Timer`, which would still leave the `.Second` comparison bugs.

### D4. Async-safe app handlers: `AwtrixApp.FireAndLog`
- `protected Task FireAndLog(Func<Task> work, string operation)` runs `work` inside an async wrapper:
  - `OperationCanceledException` → Debug log.
  - Any other exception → Error log with operation, app type and address.
  - The returned task never faults. Callers discard it (`_ = FireAndLog(...)`).
  - Synchronous throws inside `work` (for example `BuildMessage` → `_cts.Cancel()` on a disposed CTS, or `byte.Parse` overflow) are caught, because `work()` is invoked inside the try.
- Applied to every method subscribed to a tick event:
  - `DiurnalApp.ClockTickMinute`: replaces `.Result`
  - `TripTimerApp.ClockTickSecond`: replaces `.Result`; also covers `_cts.Cancel()`
  - `MqttClockRenderApp.ClockTick`: replaces the discarded, unobserved task
- Tick handlers therefore return almost immediately and the timer loop is never blocked by I/O.
- `SlackStatusApp.UserStatusChanged` also uses `.Result`, but it is a Slack socket event, not a tick. WS5 rewrites that handler, so WS1 leaves its body alone.

### D5. Publisher contract
- `AwtrixPublisher.Publish(string url, string payload)` keeps its abstract signature, so the existing test fakes still compile. The XML doc states the contract: never throws; `true` only on confirmed hand-off.
- `AwtrixPublisher` exposes `protected ILogger Logger` so subclasses log without a second field.
- `HttpPublisher`:
  - Catches every exception, logs it at Warning (type and message, no stack trace, because this is a routine offline condition) and returns `false`.
  - Logs a non-2xx status at Warning and returns `false`.
  - Disposes both `StringContent` and `HttpResponseMessage` with `using`.
- `MqttPublisher` depends on `IMqttConnector`. It returns the connector's `bool` and returns `false` if the connector throws.
- `IMqttConnector` gains `Task<bool> PublishAsync(string topic, string payload)`. `MqttConnector.PublishAsync`:
  - Returns `true` after the client publish completes.
  - Returns `false` from its catch.
  - Treats a `null` payload as empty.
  - Wraps the existing reconnect attempt in its own try/catch. WS2 removes that reconnect.
- `AwtrixService` sends every operation through one `SafePublish(baseTopic, Func<AwtrixPublisher, Task<bool>>)`. It resolves the publisher inside a try, and if a publisher breaks the contract by throwing, it logs Warning and returns `false`. A `false` result is logged at Debug here, because the publisher already logged the reason at Warning. This avoids duplicate warnings while meeting CR-12's "warn on false".
- `AwtrixService` gets an optional `ILogger<AwtrixService>? logger = null` constructor parameter, so the test code `new AwtrixService(http, mqtt)` still compiles.

### D6. `HttpPublisher` over `IHttpClientFactory`
- Constructor: `HttpPublisher(ILogger<HttpPublisher> logger, IHttpClientFactory httpClientFactory)`. There is only one constructor; the one that created its own `HttpClient` is removed.
- Named client `HttpPublisher.HttpClientName = "AwtrixHttpPublisher"`, registered with `client.Timeout = HttpPublisher.DefaultTimeout` (5 s).
- No retry or resilience handler. Tick-driven updates retry naturally on the next tick, and a retry would only display staler data.
- Test doubles `StubHttpMessageHandler` and `StubHttpClientFactory` go in `test/Test/Services/StubHttp.cs`. `FakeHttpPublisher` and `ConductorTestHelper` are updated to pass a stub factory.

### D7. Custom-app URLs are built by the publisher (CR-12)
- `AwtrixPublisher` gets `public virtual string BuildCustomAppUrl(string baseTopic, string appName)`, which defaults to `$"{baseTopic}/custom/{appName}"`, the MQTT form.
- `HttpPublisher` overrides it as `$"{baseTopic.TrimEnd('/')}/custom?name={Uri.EscapeDataString(appName)}"`.
- `AwtrixService.AppUpdate`/`AppClear` call `publisher.BuildCustomAppUrl(...)`. Notify, dismiss, settings and rtttl paths do not change.
- Transport detection moves to `AwtrixAddress.IsHttpTopic(string?)` (static) and `AwtrixAddress.IsHttp` (get-only, so the configuration binder ignores it). Scheme matching becomes case-insensitive. Before, `HTTP://…` was silently sent to MQTT; this change is compatible.

### D8. No ButtonApp for HTTP devices (CR-12)
- `Conductor.StartAsync` skips `ButtonApp` creation for `device.IsHttp` and logs at Information that hardware buttons are unsupported over HTTP. The TripTimer double-click binding is attached only when a ButtonApp exists.

### D9. DI seams (CR-35)
- `Conductor` constructor becomes: `ILogger<Conductor>, IHostEnvironment, IOptions<AwtrixConfig>, ITimerService, ITripPlannerService, IAwtrixService, ISlackConnector, IMqttConnector, IClock, ILoggerFactory`.
- `Conductor` no longer creates a new `AwtrixService` and `Clock` per app. It shares the injected singletons, which are stateless.
- New `ISlackConnector` exposes only `event EventHandler<SlackUserStatusChangedEventArgs>? UserStatusChanged` (YAGNI). `SlackStatusApp` takes `IAwtrixService` and `ISlackConnector`.
- `IAwtrixService` gains `PlayRtttl`, and `DiagnosticsController` depends on `IAwtrixService`.
- Registration moves to `public static void Program.AddAwtrixServices(IServiceCollection, IConfiguration)`, so a test can build the container with `ValidateOnBuild = true`. Interface registrations forward to the concrete singletons, which stay registered for hosted-service registration and controllers:
  - `IMqttConnector` → `MqttConnector`
  - `ISlackConnector` → `SlackConnector`
  - `ITimerService` → `TimerService`
  - `ITripPlannerService` → `TripPlannerService` (transient)
  - `IAwtrixService` → `AwtrixService` (singleton)
- Hosted-service registration order does not change: MqttConnector, SlackConnector, Conductor, TimerService.
- `Conductor.StopAsync` disposes each app inside its own try/catch. This is the minimal CR-02 shutdown fix; WS3/WS4 build on it.

### D10. `AwtrixSettings` and Diurnal hardening (from CR-01)
- `AwtrixSettings.ToString()` becomes `string.Join(";", this.Select(kv => $"{kv.Key}={kv.Value}"))`, which returns `""` when empty.
- `DiurnalApp` skips `Set` and logs at Warning when the built settings are empty.
- Minute lookup truncates the tick's `TimeOfDay` to whole minutes, so a coalesced minute tick at `06:00:07` still matches `0600`.
- WS5 should treat these three items as done.

## 5. Acceptance criteria

### CR-01: a tick-handler exception terminates the process
1. `TimerService.Tick()` never throws. If a `SecondChanged` subscriber throws, later `SecondChanged` subscribers and the `MinuteChanged` subscribers for the same tick still run. *(TimerServiceEventTests)*
2. When `IAwtrixService.Set` faults, `DiurnalApp`'s minute handler does not propagate the exception. *(DiurnalAppTests)*
3. `"2100": "Brightness=300"` at 21:00 does not throw and does not call `Set`. *(DiurnalAppTests)*
4. `"0600": "Brightnes=8"` (unknown key) does not throw on the 06:00 tick or on the startup replay after 06:00, and `Set` is never called with empty settings. *(DiurnalAppTests)*
5. `TripTimerApp`'s second handler does not propagate when `AppUpdate` faults, or when there are no future departures and `_cts` is already disposed. *(TripTimerAppTickTests)*
6. `MqttClockRenderApp.ClockTick` observes its publish task through `FireAndLog` (code review).
7. `new AwtrixSettings().ToString()` returns `""`. *(AwtrixSettingsTests)*
8. No method subscribed to `SecondChanged`/`MinuteChanged` contains `.Result` or `.Wait()` (code review).

### CR-02: HttpPublisher exceptions, timeout, disposal
1. `HttpPublisher.Publish` returns `false` without throwing on:
   - a handler `HttpRequestException`
   - a client timeout
   - a non-2xx status
   - an invalid/relative URL
   *(HttpPublisherTests)*
2. `HttpPublisher.Publish` returns `true` on 2xx, POSTs `application/json` UTF-8 to the given URL, and uses the named client `AwtrixHttpPublisher`. *(HttpPublisherTests)*
3. The response message is disposed after publish. *(HttpPublisherTests)*
4. The named client registered in DI has `Timeout == 5 s`. *(CompositionRootTests)*
5. `MqttPublisher.Publish` returns the connector's result and returns `false` when the connector throws. *(MqttPublisherTests)*
6. `AwtrixService` returns `false` and does not throw when a publisher throws. *(AwtrixServicePublishTests)*
7. `Conductor.StartAsync` completes with a real `AwtrixService` whose HTTP device is unreachable. *(ConductorTests)*
8. `Conductor.StopAsync` still disposes the other apps when one app's `Dispose` throws. *(ConductorTests)*

### CR-11: TimerService overlap, duplicate or missed ticks
1. Two `Tick()` calls within the same wall-clock second raise `SecondChanged` once. *(TimerServiceEventTests)*
2. Advancing the clock by exactly 2 minutes (same second value) raises both events. *(TimerServiceEventTests)*
3. Event time is the provider's local time, truncated to the second, with `Kind=Local`, including under a non-UTC local zone. *(TimerServiceEventTests)*
4. `StartAsync` drives ticks from the injected `TimeProvider`, and `StopAsync` completes promptly. *(TimerServiceEventTests)*
5. Only one loop invokes handlers: there is no `System.Threading.Timer` callback (code review).

### CR-12: wrong HTTP URLs for custom apps
1. For a base topic of `http://192.168.1.50/api` (with or without a trailing `/`), `AppUpdate`/`AppClear` publish to `http://192.168.1.50/api/custom?name={app}`. *(AwtrixServicePublishTests)*
2. MQTT custom-app topics stay `{base}/custom/{app}`. HTTP notify, dismiss and settings URLs do not change. *(AwtrixServicePublishTests, existing tests)*
3. App names are URL-escaped in the HTTP form. *(HttpPublisherTests)*
4. An `HTTP://` scheme (uppercase) routes to the HTTP publisher. *(AwtrixServicePublishTests, AwtrixAddressTests)*
5. No `ButtonApp` is created for HTTP devices, and MQTT devices still get one with its three subscriptions. *(ConductorTests)*

### CR-35: testability seams
1. `Conductor`, `MqttPublisher`, `SlackStatusApp` and `DiagnosticsController` have no concrete service dependencies from the list in D9 (compile-time).
2. `TimerService` takes a `TimeProvider`, and `DiurnalApp` takes an `IClock`. There is no `DateTime.Now` in either (code review).
3. The full `AddAwtrixServices` graph builds with `ValidateOnBuild`. Interface and concrete registrations resolve to the same singleton, and four hosted services are registered. *(CompositionRootTests)*
4. `Conductor.StartAsync` is covered by unit tests using mocks. *(ConductorTests)*

## 6. Testing strategy

- **TDD per task.** Write the failing test, run it filtered, implement, re-run it filtered, then run the full `dotnet test` before each commit.
- **Time:** use `FakeTimeProvider` for `TimerService` (direct `Tick()` calls give determinism; one loop test polls with a bounded timeout) and `MockClock` (existing) for apps.
- **HTTP:** use `StubHttpMessageHandler` and `StubHttpClientFactory`. Nothing touches a real socket, and the timeout test uses a 100 ms client timeout.
- **MQTT:** mock `IMqttConnector` with Moq. The connector's reconnect path is not unit-tested, because it opens real sockets; WS2 replaces it.
- **Apps:** mock `ITimerService` and raise events with `Mock.Raise`. Moq's completed-task defaults make `FireAndLog` run synchronously, so verification can follow the raise directly. Private tick handlers on `TripTimerApp` are called via reflection, following the existing `TripTimerAppBoundaryTests` style.
- **Composition:** `ServiceCollection` + `AddLogging` + mocked `IHostEnvironment` + `Program.AddAwtrixServices`, built with `ValidateOnBuild`/`ValidateScopes`. `AddControllers`/Swagger stay in `Main` and are not validated.

## 7. Seams handed to later workstreams

| Workstream | Uses |
|---|---|
| WS2 | `IMqttConnector` (with `Task<bool> PublishAsync`) as the replaceable boundary; `MqttPublisher` already reports honest results; tests use `Mock<IMqttConnector>` |
| WS3 | Interface-only `Conductor` constructor; `ConductorTestHelper.Create(...)` with injectable mocks; per-app try/catch pattern in `StopAsync`; `AddAwtrixServices` composition test |
| WS4 | `FireAndLog` for activation work; `IClock`/`FakeTimeProvider`; tick contract (D2) |
| WS5 | `IClock` in `DiurnalApp`; `ISlackConnector`; safe `AwtrixSettings.ToString`; `TimeProvider.GetTimestamp()` available for `DoubleClickDetector` |

## 8. Risks

- **Log volume when a device is offline.** TripTimer and MqttClockRender publish every second, which produces about one Warning per second per app. This is accepted for WS1 because it is visible and bounded; rate-limiting is deferred.
- **Concurrent fire-and-forget publishes.** If one publish takes longer than 1 s (the worst case is the 5 s HTTP timeout), a second-tick app can have up to about 5 overlapping publishes. They are bounded by the timeout, and each is observed. A single-flight guard belongs to WS4's activation redesign.
- **Runtime DI** is validated only through `AddAwtrixServices`. `AddControllers`/Swagger registrations are not covered, and the web host is not started in tests. Mitigation: run `dotnet run --project src/api` once after Task 5 as a manual step.
- **Package restore** of `Microsoft.Extensions.TimeProvider.Testing` 10.0.0 needs nuget.org (confirmed reachable 2026-09-13).
- **Parallel agents on `feature/redo`.** The plan touches `Conductor.cs`, `Program.cs` and `FakePublishers.cs`, which other workstreams also edit. Land WS1 before starting WS2-WS5.
