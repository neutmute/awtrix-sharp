# Codebase Concerns

**Analysis Date:** 2026-02-28

---

## Tech Debt

**`AppConfig.Name` is an alias for `Type`:**
- Issue: `Name` property is just `get => Type`, documented as "Redirect for now." Two apps of the same type on the same device cannot be distinguished. The app lookup `FindApps(appName)` also matches on `Type`, not `Name`.
- Files: `src/api/Apps/Configs/AppConfig.cs:22`, `src/api/HostedServices/Conductor.cs:234`
- Impact: Cannot run two instances of e.g. `MqttRenderApp` on the same clock. The Awtrix custom app slot name will collide and the second will overwrite the first.
- Fix approach: Add a separate `Name` field to `AppConfig`. Default it to `Type` when not provided. Update `AppFactory`, `FindApps`, and all `AppUpdate`/`AppClear` callers to use `Name` for the Awtrix slot.

**Hacky TripTimerApp ↔ ButtonApp binding in Conductor:**
- Issue: A hardcoded event wire-up inside `StartAsync` binds the first `TripTimerApp` found on a device to the right double-click. Acknowledged with `// Hacky binding for now` comment.
- Files: `src/api/HostedServices/Conductor.cs:95-105`
- Impact: Only the last `TripTimerApp` across all devices gets wired. Binding logic lives in the orchestrator, not in an app or config.
- Fix approach: Define an interface or config-driven mechanism (e.g. `OnDoubleClickRight: TripTimerApp`) to declare these bindings in `appsettings.json`.

**`AwtrixService` instantiated with `new` inside Conductor's `AppFactory`:**
- Issue: `Conductor.AppFactory` calls `new AwtrixService(_httpPublisher, _mqttPublisher)` for each app, bypassing the DI container. Each app creation creates a new service instance.
- Files: `src/api/HostedServices/Conductor.cs:137`
- Impact: Prevents mocking `AwtrixService` in integration tests. Breaks DI scoping model. Transient registration in `Program.cs` is effectively unused for apps.
- Fix approach: Inject `IAwtrixService` via DI into `AppFactory` or use `IServiceProvider` to resolve it.

**`HttpPublisher` uses `new HttpClient()` directly:**
- Issue: `HttpPublisher` creates its own `HttpClient` instance in the constructor. The codebase uses `IHttpClientFactory` for the Transport NSW clients but not for the Awtrix HTTP transport.
- Files: `src/api/Services/HttpPublisher.cs:12`
- Impact: Sockets are not pooled; DNS changes are not honoured; can lead to socket exhaustion under load.
- Fix approach: Inject `IHttpClientFactory` and call `CreateClient()`.

**`SlackConnector` uses `static` fields for instance state:**
- Issue: `_slackApiClient` and `_slackSocketClient` are declared `static`, meaning they are class-level state. If `SlackConnector` is ever registered as transient or re-created, state leaks across instances.
- Files: `src/api/HostedServices/SlackConnector.cs:23-24`
- Impact: Subtle bugs if the DI lifetime ever changes; prevents unit testing with isolated instances.
- Fix approach: Remove `static` modifier — both fields should be instance fields.

**`Newtonsoft.Json.Linq` imported but unused:**
- Issue: Both `SlackConnector.cs` and `TripPlannerService.cs` import `using Newtonsoft.Json.Linq;` but contain zero usages of `JObject`, `JToken`, or `JArray`. The project uses `System.Text.Json` everywhere else.
- Files: `src/api/HostedServices/SlackConnector.cs:2`, `src/api/Services/TripPlanner/TripPlannerService.cs:4`
- Impact: Unnecessary dependency on Newtonsoft at runtime; misleading to readers.
- Fix approach: Remove the unused `using` directives.

**Commented-out file-path debug code in `TripPlannerController`:**
- Issue: A 3-line block for writing departures to `D:\downloads\departures.json` is commented out in production code.
- Files: `src/api/Controllers/TripPlannerController.cs:55-57`
- Impact: Dev noise; hardcoded Windows path would break on Linux Docker deployment if accidentally uncommented.
- Fix approach: Delete the commented block; use the cache mechanism (`AWTRIXSHARP_SETTINGS__DATA_DIRECTORY`) which already exists for this purpose.

**`AppConfig.ConvertValue` uses invariant `Parse` instead of `TryParse`:**
- Issue: All numeric/date conversions in `ConvertValue` call hard `int.Parse`, `double.Parse`, `DateTime.Parse`, etc. without try/catch at the call site.
- Files: `src/api/Apps/Configs/AppConfig.cs:131-155`
- Impact: A bad value in `appsettings.json` causes an unhandled `FormatException` at startup, with a cryptic stack trace rather than a clear configuration error.
- Fix approach: Use `TryParse` variants and throw `InvalidOperationException` with the key name and bad value for actionable error messages.

---

## Known Bugs

**Multi-device `Init()` loop re-initialises devices from previous iterations:**
- Symptoms: When two or more devices are configured, apps from device #1 are initialised again (a second time) when processing device #2 — because `_apps` accumulates across the outer loop and the inner `foreach(var app in _apps)` iterates the entire accumulated list.
- Files: `src/api/HostedServices/Conductor.cs:110-113`
- Trigger: Configure `Awtrix.Devices` with two or more entries.
- Workaround: Run with a single device configured.

**`TripPlannerService.GetNextDepartures` uses only `DepartureTimeEstimated`, ignoring real-time data:**
- Symptoms: Departures show the estimated (scheduled) time, not the actual real-time departure. The API is called with `tfNSWTR: TfNSWTR.True` to enable real-time, but the parsing code reads `DepartureTimeEstimated` instead of `DepartureTimeEstimated` (real-time equivalent or `DepartureTimePlanned`).
- Files: `src/api/Services/TripPlanner/TripPlannerService.cs:106`
- Trigger: Active real-time delays in the Transport NSW system.
- Workaround: None at runtime; real-time delays are silently ignored.

**`TripTimerApp.ClockTickSecond` blocks on async via `.Result`:**
- Symptoms: `_ = AppUpdate(message).Result` is called on a `System.Threading.Timer` callback thread. If the MQTT/HTTP publish is slow or the thread pool is saturated, this blocks a thread pool thread and can cause cascading delays.
- Files: `src/api/Apps/TripTimer/TripTimerApp.cs:74`
- Trigger: High system load or slow MQTT broker.
- Workaround: None.

---

## Security Considerations

**Swagger UI and `DeveloperExceptionPage` enabled unconditionally in production:**
- Risk: The comment `// if (app.Environment.IsDevelopment()) always show swagger` explicitly overrides the environment guard. The Swagger UI exposes all API endpoints and `DeveloperExceptionPage` returns full stack traces in HTTP 500 responses.
- Files: `src/api/Program.cs:80-86`
- Current mitigation: The service is expected to be accessed only on a private LAN, not exposed to the internet.
- Recommendations: Restore the `IsDevelopment()` check, or add an explicit opt-in config flag (`Swagger:Enabled: true`), and move `DeveloperExceptionPage` behind the development guard.

**API controllers have no authentication or authorization:**
- Risk: All controller endpoints (`/Diagnostics`, `/api/TripPlanner`, `/api/app/TripTimer`, `/api/app/MqttRender`) are completely unauthenticated. Any client on the network can trigger MQTT publishes, retrieve stop/trip data, or force app execution.
- Files: `src/api/Controllers/DiagnosticsController.cs`, `src/api/Controllers/TripPlannerController.cs`, `src/api/Controllers/TripTimerController.cs`
- Current mitigation: Service is bound to LAN only.
- Recommendations: Add an API key middleware or at minimum an `[AllowAnonymous]` + warning if ever exposed.

**`Environment.GetEnvironmentVariable` called directly, bypassing `IConfiguration`:**
- Risk: Secret values are read inconsistently. `SlackConnector` reads `AWTRIXSHARP_SLACK__APPTOKEN` directly from the environment, while other secrets use `IOptions<T>`. User Secrets (dev) and `appsettings.json` overrides do not apply to direct `GetEnvironmentVariable` calls.
- Files: `src/api/HostedServices/SlackConnector.cs:53`, `src/api/Program.cs:36`, `src/api/Services/TripPlanner/TripPlannerService.cs:124`, `src/api/Apps/Configs/AppConfigKeys.cs:29`
- Current mitigation: Works as long as all secrets are provided via environment variables.
- Recommendations: Route all config through `IOptions<T>` / `IConfiguration` for consistent User Secrets and environment variable layering.

---

## Performance Bottlenecks

**`TimerService` polls at 100ms interval on ThreadPool timer:**
- Problem: `CheckTimeChange` fires every 100ms from a `System.Threading.Timer`, which dispatches on the thread pool. With multiple apps subscribing to `SecondChanged`/`MinuteChanged`, each second fires synchronous event handlers on a thread pool thread.
- Files: `src/api/HostedServices/TimerService.cs:65`, `src/api/Apps/TripTimer/TripTimerApp.cs:73-74`
- Cause: Clock-tick events invoke `AppUpdate(...).Result`, blocking the thread pool thread for the duration of each MQTT/HTTP publish.
- Improvement path: Make clock-tick handlers `async` and let the `TimerService` await handlers, or use a `Channel<T>` to dispatch work off the timer thread.

**`ValueMap.ApplyDynamicProperty` uses reflection on every MQTT message:**
- Problem: For any `ValueMap` key not explicitly handled, `ApplyDynamicProperty` calls `typeof(AwtrixAppMessage).GetMethods()` via reflection to resolve the setter. This runs on every MQTT message delivery when a ValueMap is active.
- Files: `src/api/Apps/Configs/ValueMap.cs:116-173`
- Cause: Open-ended dynamic dispatch to handle arbitrary `AwtrixAppMessage` setters.
- Improvement path: Build a `static readonly Dictionary<string, Action<AwtrixAppMessage, string>>` lookup at startup to replace the per-call reflection.

---

## Fragile Areas

**`ScheduledApp._cts` field is not thread-safe:**
- Files: `src/api/Apps/ScheduledApp.cs`
- Why fragile: `_cts` is read and written from both the background `Task.Run` loop and foreground calls to `ExecuteNow()` / `Dispose()`. No synchronization primitive guards concurrent access. A race between `ExecuteNow` (which replaces `_cts`) and the background loop (which reads `_cts.Token`) can leave a cancelled token in the new CTS.
- Safe modification: Wrap `_cts` replacement with a `lock` or use `Interlocked.Exchange`.
- Test coverage: No tests cover concurrent `ExecuteNow` + schedule interactions.

**`ButtonApp.Initialize()` uses `.Wait()` on MQTT Subscribe:**
- Files: `src/api/Apps/Buttons/ButtonApp.cs:55`
- Why fragile: `_mqttConnector.Subscribe(buttonState.Topic).Wait()` is called from `Init()`, which is called from `Conductor.StartAsync`. Blocking here on `StartAsync` delays the hosted service startup sequence. If MQTT is slow to respond it blocks the entire ASP.NET startup pipeline.
- Safe modification: `Init()` should be `InitAsync()` (returning `Task`) so callers can `await` it. This is noted in project memory as a prior breaking change.
- Test coverage: None.

**`Conductor.ExecuteNow` creates a transient app outside the app registry:**
- Files: `src/api/HostedServices/Conductor.cs:203-229`
- Why fragile: `ExecuteNow` calls `AppFactory` to create a fresh app instance, calls `Init()` on it, then calls `ExecuteNow()` on it — but this transient app is never added to `_apps`, never disposed, and shares the same Awtrix slot name as the live app. Race conditions between the live scheduled app and the on-demand app publishing to the same MQTT topic are possible.
- Safe modification: Route `ExecuteNow` through the existing `_apps` registry instead of creating a new instance.
- Test coverage: None.

**`MqttClockRenderApp` has a potential race on `_mqttValue` / `_currentTime`:**
- Files: `src/api/Apps/MqttRender/MqttClockRenderApp.cs:16-17`
- Why fragile: `_mqttValue` is written from the MQTT thread (via `HandleMessage`) and read from the `TimerService` callback thread (via `ClockTick → UpdateDisplay`). Both are plain fields with no `volatile` or synchronisation.
- Safe modification: Mark fields `volatile` or use `Interlocked`; alternatively, ensure `UpdateDisplay` is only called from one path.
- Test coverage: None.

---

## Scaling Limits

**Single `_apps` list shared across all devices:**
- Current capacity: Works for 1 device; partially broken for 2+ (see Init loop bug above).
- Limit: Apps are all in one flat list; `FindApps` returns all matching by type across all devices without device filtering.
- Scaling path: Replace `List<IAwtrixApp>` with `Dictionary<string, List<IAwtrixApp>>` keyed by `BaseTopic`.

**Trip planner caches by hour of day only:**
- Current capacity: Cache file name is `trip_{origin}_{dest}_{HH}.json`, so cache is re-used for all minutes within the same hour.
- Limit: Stale cache can return past departures when replayed with today's date but wrong minute.
- Scaling path: Cache by origin/destination/date or add a validity check against `fromWhen`.

---

## Dependencies at Risk

**`TripPlannerClient.nswag.cs` is a 5,607-line generated file with known bugs:**
- Risk: The file contains comments `// BUGGY SWAGGER SPEC MEANS THIS BLOWS UP SOMETIMES` and multiple `/// XXX` markers indicating unresolved upstream API spec issues. The file is generated from the Transport NSW OpenAPI spec and cannot easily be patched manually.
- Files: `src/transportOpenData/TripPlanner/TripPlannerClient.nswag.cs:3115`
- Impact: Deserialisation can throw unexpectedly on certain API responses (e.g. ferry routes with `SUPPLEMENT` value).
- Migration plan: Wrap all calls to the generated client in try/catch; add a `JsonException` handler in `TripPlannerService.GetNextDepartures`; consider regenerating from a corrected spec version.

**`SlackNet` SDK locked to Socket Mode only:**
- Risk: The commented-out `_slackApiClient` block (`SlackConnector.cs:68-71`) indicates the API client initialisation was attempted but abandoned. User presence polling and DND status listening rely on `UserChange` events via Socket Mode, which requires an App-Level Token — a more privileged and harder-to-rotate credential than bot tokens.
- Files: `src/api/HostedServices/SlackConnector.cs:68-71`
- Impact: DND status is not tracked (the `UserDnChanged` event is declared but never fired). Slack revocation of the App-Level Token disables the entire Slack integration silently.
- Migration plan: Implement `UserDnChanged` event handling or document it as unsupported; add startup validation that the Slack token is valid.

---

## Test Coverage Gaps

**No tests for `Conductor` (startup, multi-device, factory):**
- What's not tested: App creation per device config, `StartAsync` Init loop, `ExecuteNow` transient-app path, `FindApps` filtering.
- Files: `src/api/HostedServices/Conductor.cs`
- Risk: The multi-device Init loop bug (described above) is undetected by tests.
- Priority: High

**No tests for `MqttConnector` reconnect logic:**
- What's not tested: Reconnect on publish failure (`ConnectAsync` called from `PublishAsync`), timeout behaviour, `StopAsync` when `_client` is in disconnected state.
- Files: `src/api/HostedServices/MqttConnector.cs`
- Risk: A misconfigured MQTT host hangs startup indefinitely in the worst case, or silently drops messages after reconnect.
- Priority: High

**No tests for `SlackConnector` or `SlackStatusApp`:**
- What's not tested: `UserStatusChanged` event propagation, ValueMap matching for emoji vs. text, empty status clearing display.
- Files: `src/api/HostedServices/SlackConnector.cs`, `src/api/Apps/SlackStatus/SlackStatusApp.cs`
- Risk: Changes to event handler or ValueMap lookup break silently.
- Priority: Medium

**No tests for `DiurnalApp` config parsing:**
- What's not tested: Time format parsing (`hhmm`), setting key dispatch (`brightness`, `globalTextColor`), replay of past settings on startup.
- Files: `src/api/Apps/Diurnal/DiurnalApp.cs`
- Risk: A bad time entry in `appsettings.json` causes a swallowed exception that produces no brightness changes with no clear error.
- Priority: Medium

**No tests for `HttpPublisher` or `MqttPublisher`:**
- What's not tested: HTTP publish path, MQTT publish path, publisher selection logic in `AwtrixService.ResolvePublisher`.
- Files: `src/api/Services/HttpPublisher.cs`, `src/api/Services/MqttPublisher.cs`, `src/api/Services/AwtrixService.cs:100-110`
- Risk: A regression in publisher routing (e.g. topic prefix detection) would silently drop all messages.
- Priority: Medium

---

*Concerns audit: 2026-02-28*
