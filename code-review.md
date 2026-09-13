# AwtrixSharp Code Review (consolidated)

**Date:** 2026-09-13
**Branch:** `feature/redo` @ `9c5a64f`

## Scope and method

- **Source reviews:** three independent reviews of `4bf0d47`:
  - Core: HostedServices, Program, Services, Controllers, Domain, Docker/CI
  - Apps: `src/api/Apps`, AwtrixAppMessage, appsettings, tests
  - Trip planner: TripPlanner service, TransportOpenData, TripTimer
- **What changed since:** between `4bf0d47` and HEAD only these changed:
  - all csproj files and the Dockerfile moved to net10.0
  - `Microsoft.AspNetCore.OpenApi` was removed, along with the stale `using Microsoft.OpenApi.Attributes;` in TripTimerController, so every TripTimerController line after 5 moved up by one
- **Dropped:** review remarks about net9, TFM split and stale memory notes.
- **Still true on HEAD:** `IAwtrixApp.Init()` is synchronous and `HttpPublisher` still does `new HttpClient()`.
- **Line references** were spot-checked against HEAD and corrected where needed.

**Constraint:** every fix assigned to a workstream must keep existing `appsettings.json` keys, the `AWTRIXSHARP_*` env vars and `TRANSPORTOPENDATA__APIKEY` working unchanged. New settings must be optional, with defaults that keep today's behaviour unless that behaviour is the bug. Fixes that need a breaking config change are listed under **Deferred**.

**Severity levels:**
- **Critical:** crashes the process or stops the host starting, from realistic config or a transient fault.
- **High:** a feature silently stops working, or resources leak without bound.
- **Medium:** wrong behaviour in plausible conditions, or a security exposure.
- **Low:** latent bugs, cleanup, test gaps.

**Counts:** Critical 2 · High 8 · Medium 19 · Low 16 (45 total)

---

## Summary

| ID | Sev | Title | Area | WS |
|---|---|---|---|---|
| CR-01 | Critical | Exception in a clock-tick handler terminates the process | TimerService / apps | WS1 |
| CR-02 | Critical | HttpPublisher: exceptions escape, 100 s timeout, responses not disposed | Services | WS1 |
| CR-03 | High | MQTT reconnect replaces client, so handlers and subscriptions are lost; disconnects go undetected | MqttConnector | WS2 |
| CR-04 | High | `POST /Mqtt/publish` reconnects and destroys the live MQTT client | Controllers | WS2 |
| CR-05 | High | Broker or device down at boot: slow start then host crash; one bad app kills all | Conductor / init | WS3 |
| CR-06 | High | Conductor re-initialises earlier devices' apps once per later device | Conductor | WS3 |
| CR-07 | High | `Conductor.ExecuteNow` leaks a cron-armed duplicate app per call | Conductor / controllers | WS3 |
| CR-08 | High | ScheduledApp `_cts` / scheduling not re-entrant: duplicate waiters, orphaned CTS, dead schedule | ScheduledApp | WS4 |
| CR-09 | High | MqttClockRenderApp never unsubscribes `SecondChanged` (one extra handler per day) | Apps | WS4 |
| CR-10 | High | One malformed journey or an error body throws and loses the whole trip-timer window | TripPlannerService | WS6 |
| CR-11 | Medium | TimerService callbacks overlap: duplicate or missed ticks | TimerService | WS1 |
| CR-12 | Medium | HTTP transport builds wrong URLs for custom apps (`/custom/{name}`) | AwtrixService | WS1 |
| CR-13 | Medium | Config precedence inverted: appsettings.json re-added last | Program | WS7 |
| CR-14 | Medium | Secrets read with `Environment.GetEnvironmentVariable`, bypassing IConfiguration | Program / Slack / Trip | WS7 |
| CR-15 | Medium | DeveloperExceptionPage always on; bad input returns 500 with a stack trace | Program / controllers | WS7 |
| CR-16 | Medium | `SlackConnector.StopAsync` throws NRE when Slack is not configured | SlackConnector | WS3 |
| CR-17 | Medium | CI never runs tests | CI | WS8 |
| CR-18 | Medium | `ExecuteNow` runs have no ActiveTime limit | ScheduledApp | WS4 |
| CR-19 | Medium | TripTimer "no departures" publishes `{}` and cancels via the shared `_cts` field | TripTimerApp | WS4 |
| CR-20 | Medium | Diurnal: startup replay ignores yesterday; a missed minute skips that setting | DiurnalApp | WS5 |
| CR-21 | Medium | Diurnal setting values validated lazily; empty settings crash `ToString` | DiurnalApp / AwtrixSettings | WS5 |
| CR-22 | Medium | App config and ValueMap key lookup is case-sensitive, so env-var overrides are ignored | Configs | WS7 |
| CR-23 | Medium | Typed config values parsed late with current culture; a failure permanently stops the app | AppConfig | WS7 |
| CR-24 | Medium | SlackStatusApp NRE on every user_change when no user id is configured | SlackStatusApp | WS5 |
| CR-25 | Medium | Departures fetched once per activation: no refresh, no retry | TripTimerApp | WS6 |
| CR-26 | Medium | Trip query and schedules depend on host timezone (container defaults to UTC) | Trip / scheduling | WS6 |
| CR-27 | Medium | Leading walking leg treated as the train departure | TripPlannerService | WS6 |
| CR-28 | Medium | Cancelled services not detected (`realtimeStatus` not modelled) | TransportOpenData | WS6 |
| CR-29 | Medium | Typed HttpClients captured by singleton; no timeout, retry or cancellation | Program / Trip | WS6 |
| CR-30 | Low | Unknown app `Type` / missing CronSchedule crashes host with unhelpful error | Conductor | WS3 |
| CR-31 | Low | Dispose pattern broken (method hiding, fire-and-forget clears, no unsubscribes) | App base classes | WS4 |
| CR-32 | Low | DoubleClickDetector uses `DateTime.Now` (DST); double-click also fires Click | ButtonApp | WS5 |
| CR-33 | Low | MqttRenderApp attaches handler after subscribe, so the first retained message can be missed | MqttRenderApp | WS2 |
| CR-34 | Low | ValueMap: double/array setters silently ignored; arrays serialised as strings; invalid regex silent | ValueMap / AwtrixAppMessage | WS5 |
| CR-35 | Low | Testability seams missing (concrete deps, `DateTime.Now` in TimerService) | Cross-cutting | WS1 |
| CR-36 | Low | `TransportOpenData:BaseUrl` is dead config | Program / Trip | WS6 |
| CR-37 | Low | Trip file cache: bypasses live data, midnight rollover, crashes on bad file | TripPlannerService | WS6 |
| CR-38 | Low | Strict enum converters in generated model fail the whole request on new values | TransportOpenData | WS6 |
| CR-39 | Low | Test gaps: ValueMap, ScheduledApp lifecycle, MqttClockRenderApp, time-dependent tests | Tests | WS4 |
| CR-40 | Low | Test gaps: no TripPlannerService tests; fixture tests use a different serializer config | Tests | WS6 |
| CR-41 | Low | SlackConnector static client fields, dead `_slackApiClient` / `UserDnChanged` | SlackConnector | WS8 |
| CR-42 | Low | Misc Conductor/Domain defects (logger category, "Text" ordering, dead branch) | Conductor / Domain | WS8 |
| CR-43 | Low | Dead or misleading code and lossy error logging | Cross-cutting | WS8 |
| CR-44 | Low | `TripSummary.ToString` shows wrong duration for trips of an hour or more | TripSummary | WS8 |
| CR-45 | Low | Dockerfile restore layer ineffective; `EXPOSE 8081`, HTTPS redirection, dead SlackUserHarvester | Docker / Program | WS8 |

---

## Findings

### Critical

#### CR-01. Exception in a clock-tick handler terminates the process
- **Severity:** Critical
- **Sources:** core C1 and apps C1
- **Refs:**
  - `src/api/HostedServices/TimerService.cs:65`: `System.Threading.Timer` callback
  - `TimerService.cs:88-121`: `CheckTimeChange`, `OnSecondChanged` and `OnMinuteChanged` have no try/catch
  - Blocking handlers:
    - `Apps/Diurnal/DiurnalApp.cs:127`: `Set(...).Result`
    - `Apps/TripTimer/TripTimerApp.cs:74`: `AppUpdate(...).Result`
    - `TripTimerApp.cs:88`: `_cts.Cancel()`
    - `Apps/MqttRender/MqttClockRenderApp.cs:41`: discarded task
- **Problem:** Handlers run on a thread-pool timer thread. An unhandled exception there ends the .NET process. It also skips the remaining subscribers in the multicast delegate.
- **Failure scenarios (all verified):**
  1. **Unknown key in Diurnal config**, e.g. `"0600": "Brightnes=8"`:
     - The 06:00 entry gets an empty action list.
     - `ClockTickMinute` logs `{awtrixSetting}`, so `AwtrixSettings.ToString()` runs `.Aggregate` on an empty sequence (`Domain/AwtrixSettings.cs:25-27`) and throws `InvalidOperationException`. The process exits.
     - If the service starts after 06:00, the same exception comes out of the startup replay (`DiurnalApp.cs:107`), so `Conductor.StartAsync` fails.
  2. **Out-of-range value:** `"2100": "Brightness=300"` → `byte.Parse` throws `OverflowException` at 21:00 (`DiurnalApp.cs:70`).
  3. **HTTP device offline:** `HttpPublisher` throws (CR-02) → `.Result` throws `AggregateException` on the timer thread.
  4. **Disposed CTS:** `TripTimerApp.cs:88` `_cts.Cancel()` on a CTS already disposed by `ExecuteNow`/`ScheduleNextWakeUp` throws `ObjectDisposedException` (CR-08).
- **Fix:**
  - In `CheckTimeChange`, iterate `GetInvocationList()` and call each subscriber inside its own try/catch that logs.
  - Change app handlers to async-safe wrappers: `async void` with try/catch, or queue the work to a `Channel`, instead of `.Result`.
  - Make `AwtrixSettings.ToString()` use `string.Join(";", ...)`.
  - Skip `Set` when the settings object is empty.
- **Config compat:** No change.

#### CR-02. HttpPublisher: exceptions escape, 100 s timeout, responses not disposed
- **Severity:** Critical
- **Source:** core C2
- **Refs:**
  - `src/api/Services/HttpPublisher.cs:12-19`
  - Blocking callers:
    - `Apps/AwtrixApp.cs:33` (`Init` → `AppClear().Result`)
    - `AwtrixApp.cs:80` (`Dispose` → `.Wait()`)
    - timer handlers (CR-01)
  - `HostedServices/Conductor.cs:241` (`_apps.ForEach(a => a.Dispose())`)
- **Problem:**
  - `PostAsync` failures are not caught: `HttpRequestException`, and `TaskCanceledException` on timeout. `MqttConnector.PublishAsync` swallows its failures, so the two transports behave differently.
  - The timeout is the default 100 s.
  - `HttpResponseMessage` and `StringContent` are never disposed.
- **Failure scenarios:**
  - **Device unplugged at startup:** `Conductor.StartAsync` throws and the whole host fails to start, including the MQTT devices.
  - **Shutdown:** `ForEach(Dispose)` stops at the first throwing app, so the remaining apps are never disposed.
  - **Slow or unreachable device:** each 1 s tick blocks a thread-pool thread for up to 100 s. This starves the pool and amplifies CR-11.
- **Fix:**
  - Catch `HttpRequestException` and `TaskCanceledException`, log them and return `false`. Publishers must never throw.
  - Use `using var` for the response and the content.
  - Use `IHttpClientFactory` (named client) with a short timeout (5 s).
  - Make Conductor's per-app Dispose loop exception-safe (see CR-31).
- **Config compat:** No change.

### High

#### CR-03. MQTT reconnect replaces client, so handlers and subscriptions are lost; disconnects go undetected
- **Severity:** High
- **Sources:** core H1 and apps H3
- **Refs:** `src/api/HostedServices/MqttConnector.cs`
  - `:20-24`: event accessors forward to the *current* `_client`
  - `:50`: `ConnectAsync` creates a new client every time
  - `:101-107`: a publish failure triggers reconnect; the message is dropped
  - `:122`: `Subscribe` with no record of topics
  - `Services/MqttPublisher.cs:18-19`: always returns `true`
  - Consumers: `Apps/Buttons/ButtonApp.cs:55-59`, `Apps/MqttRender/MqttRenderApp.cs:34-35,53`
- **Problem:**
  - Handlers and subscriptions stay bound to the old `IMqttClient`.
  - Nothing replays subscriptions after a clean-start session.
  - The old client is never disposed, so its TCP connection leaks.
  - There is no `DisconnectedAsync` handling and no reconnect loop. MQTTnet 5 has no managed client, so reconnect happens only when a later publish fails.
  - A failed publish is lost, but reported as success.
  - `MessageReceived` dereferences a null `_client` if anything subscribes before `StartAsync`.
- **Failure scenario:**
  1. The broker restarts at 03:00. ButtonApp and MqttRenderApp stop receiving messages.
  2. At 06:10 a TripTimer publish fails, and a fresh client is created with no handlers and no subscriptions.
  3. Buttons, the double-click → TripTimer binding, MqttRenderApp and MqttClockRenderApp stay dead until the process restarts.
  4. The only trace is a single "Failed to publish" log line.
  5. `Deactivate`'s `-=` runs against the new client and does nothing.
- **Fix:**
  - Create the client once, in the constructor, and reconnect using the same instance.
  - Keep a set of subscribed topics and re-subscribe on every `ConnectedAsync`.
  - Handle `DisconnectedAsync` with a background reconnect loop with backoff.
  - Guard connects with a `SemaphoreSlim`.
  - Make `Subscribe` record the topic and not throw while disconnected.
  - Make `PublishAsync` return `bool`, and propagate it through `MqttPublisher`.
  - Dispose the client on stop.
- **Config compat:** No change (same `Mqtt:*` settings).

#### CR-04. `POST /Mqtt/publish` reconnects and destroys the live MQTT client
- **Severity:** High
- **Source:** core H2 (the security aspect is listed under Deferred)
- **Refs:** `src/api/Controllers/MqttController.cs:22`
- **Problem:** Every request calls `ConnectAsync()`. Because of CR-03, this swaps in a new client without handlers or subscriptions.
- **Failure scenario:** One test call from Swagger permanently kills every button and MQTT-render app.
- **Fix:** Remove the `ConnectAsync` call; the connector owns the connection. Return 503 when publishing fails (once CR-03 returns `bool`).
- **Config compat:** No change.

#### CR-05. Broker or device down at boot: slow start then host crash; one bad app kills all
- **Severity:** High
- **Sources:** core H5 and apps H4
- **Refs:**
  - `HostedServices/MqttConnector.cs:110-113`: `StartAsync` swallows the connect failure
  - `Apps/Buttons/ButtonApp.cs:55`: `Subscribe(...).Wait()`
  - `Apps/AwtrixApp.cs:31-38`: sync `Init` → `AppClear().Result`
  - `HostedServices/Conductor.cs:71-117`: no per-app try/catch
- **Problem:**
  - With the broker down, each app's `AppClear` publish fails, and `PublishAsync` then blocks on a 10 s reconnect (per app).
  - `ButtonApp`'s `SubscribeAsync` on a disconnected client throws `AggregateException`, which escapes `StartAsync`.
  - On the HTTP transport, an offline device fails `Init` the same way (CR-02).
- **Failure scenario:**
  1. The Docker host reboots and the container starts before Mosquitto.
  2. Startup blocks for tens of seconds, then the host exits.
  3. The container crash-loops until the broker is up.
- **Fix:**
  - Introduce `Task InitAsync()` on `IAwtrixApp`, and have `Conductor.StartAsync` await it.
  - Wrap each app's init in try/catch, log the device and app type, and continue.
  - Depends on CR-03 (subscribe while disconnected is deferred, not thrown) and CR-02 (publishers never throw).
- **Config compat:** No change.

#### CR-06. Conductor re-initialises earlier devices' apps once per later device
- **Severity:** High
- **Source:** core H3
- **Refs:** `src/api/HostedServices/Conductor.cs:110-113`. The `foreach (var app in _apps) app.Init();` loop sits inside the per-device loop, and `_apps` is cumulative.
- **Problem:** With N devices, device 1's apps are initialised N times. Init is not idempotent:
  - ButtonApp re-subscribes and adds `RawMessageReceived` and the Click/DoubleClick forwarders again (`ButtonApp.cs:53-59`).
  - DiurnalApp adds `MinuteChanged` again (`DiurnalApp.cs:34`), duplicates the action map and replays again.
  - SlackStatusApp adds `UserStatusChanged` again (`SlackStatusApp.cs:27`).
  - ScheduledApp calls `ScheduleNextWakeUp` again.
- **Failure scenario:** With two clocks, one right double-click on clock 1 calls `tripTimerApp.ExecuteNow()` twice. Each call disposes the other's CTS (CR-08), so the trip timer starts and immediately cancels. Diurnal and Slack updates are sent twice.
- **Fix:** Move the init loop after the device loop, or init only the apps created for the current device. Add a guard so `Init` runs at most once per app.
- **Config compat:** No change.

#### CR-07. `Conductor.ExecuteNow` leaks a cron-armed duplicate app per call
- **Severity:** High
- **Sources:** core H4 and core L4
- **Refs:**
  - `src/api/HostedServices/Conductor.cs:203-230`
  - callers: `Controllers/TripTimerController.cs:27-30`, `Controllers/MqttRenderController.cs:40-41`
- **Problem:** `ExecuteNow` builds a fresh app with `AppFactory`, then calls `Init()` (which arms the cron waiter or adds event handlers) and `ExecuteNow()`. The app is never added to `_apps` and never disposed. After its run, `ScheduleNextWakeUp` re-arms the cron, so the orphan lives forever.
- **Also:**
  - The controllers return 200 ("App started successfully") or `void` even when the device or app is not found, or when it throws.
  - `Init()` blocks the request thread on `.Result`.
- **Failure scenario:** Three calls to `POST /api/app/TripTimer/start` leave 4 live TripTimerApps. Every weekday at 06:10 all 4 call TfNSW and publish into the same `custom/TripTimerApp` slot every second, until the service restarts.
- **Fix:**
  - Look up the existing instance in `_apps` by device (store `DeviceConfig`/BaseTopic per app) and type, then call its `ExecuteNow()`.
  - Return a result enum (NotFound / Started / Error), which the controllers map to 404, 200 or 500.
- **Config compat:** No change. The API returns 404 where it returned a misleading 200; the routes are unchanged.

#### CR-08. ScheduledApp `_cts` / scheduling not re-entrant
- **Severity:** High
- **Sources:** apps H1; core CONCERNS verification (`_cts` race)
- **Refs:** `src/api/Apps/ScheduledApp.cs`
  - `:12`: shared `_cts`
  - `:38-68`: Dispose
  - `:70-98`: `ScheduleNextWakeUp`
  - `:100-118`: `WaitForCronSchedule`
  - `:120-126`: `ExecuteNow`
  - `:128-149`: `WakeUp`
  - `WaitForCancellation`: TCS without `RunContinuationsAsynchronously`
  - `Apps/TripTimer/TripTimerApp.cs:88`
- **Problem:**
  - Three paths share `_cts` and `IsScheduled` with no synchronisation: the cron waiter, `ExecuteNow`, and the `finally` in `WakeUp`.
  - `Cancel()` runs the old run's `finally` → `ScheduleNextWakeUp` inline, which disposes and replaces whatever `_cts` is at that moment.
  - A failure inside `WaitForCronSchedule` (e.g. CR-23) is caught at `:92-96` and never reschedules.
  - `Task.Delay(delay)` throws for delays over about 49.7 days (e.g. a yearly cron). That is swallowed the same way, so the app never reschedules.
- **Failure scenarios:**
  - **A: ExecuteNow during an active run.** The old continuation creates CTS2 and a new cron waiter. `ExecuteNow` then overwrites `_cts` with CTS3, so CTS2 is orphaned and cannot be cancelled even at shutdown. When the manual run ends, a second waiter is started. The next cron fires two concurrent `WakeUp`s, and the count grows with each repeat.
  - **B: two ExecuteNow calls while the TfNSW call is in flight.** The first run's `cts.Token` throws `ObjectDisposedException`. Its `finally` cancels the second run, so the second press does nothing.
  - **C: shutdown during a run.** `finally` → `Dispose(_cts)` on an already disposed CTS throws `ObjectDisposedException`.
- **Fix:**
  - Give each activation its own local CTS, passed down; never read `_cts` inside a run.
  - Swap CTS under a lock, and add a `_disposed` flag checked by `ScheduleNextWakeUp`.
  - Create the TCS with `RunContinuationsAsynchronously`, or use `Task.Delay(Infinite, token)`.
  - Link every waiter to an app-lifetime CTS.
  - Always reschedule after a waiter failure.
  - Wait in chunks of at most `int.MaxValue` ms.
- **Config compat:** No change.

#### CR-09. MqttClockRenderApp never unsubscribes `SecondChanged`
- **Severity:** High
- **Sources:** apps H2; core out-of-scope note
- **Refs:**
  - `src/api/Apps/MqttRender/MqttClockRenderApp.cs:33` (`+=`, with no `-=` anywhere)
  - `:41` (discarded `UpdateDisplay()` task)
  - `Apps/MqttRender/MqttRenderApp.cs:51-55` (`Deactivate` is private and only removes the MQTT handler)
- **Problem:** Every activation adds `ClockTick` again, and it keeps publishing after deactivation.
- **Failure scenario:** With the shipped config (`0 0 * * *`, ActiveTime 23:59):
  1. At 23:59 the custom app is cleared, and one second later `ClockTick` republishes it.
  2. After N days the clock receives N publishes per second, and broker and device load keep growing.
- **Fix:**
  - Make the deactivation hook `protected virtual`, and override it to remove `ClockTick`, or unsubscribe in a `finally` around `base.ActivateScheduledWork`.
  - Observe the `UpdateDisplay()` task.
- **Config compat:** No change.

#### CR-10. One malformed journey or an error body throws and loses the whole trip-timer window
- **Severity:** High
- **Source:** trip H1
- **Refs:**
  - `src/api/Services/TripPlanner/TripPlannerService.cs:92-115`
  - fixtures `test/transportOpenData.Tests/TestData/ErrorResponse.json`, `SuccessfulTripResponse.json`
- **Problem:** There is no check for `trips.Error`, a null `Journeys`, empty `Legs`, or null time strings.
  - `foreach (trips.Journeys)` throws an NRE on an error body. `ErrorResponse.json` has `error` and no `journeys` (verified).
  - `Legs.First()` throws on empty legs.
  - `DateTimeOffset.Parse(origin.DepartureTimeEstimated)` throws when the estimate is absent. The first-leg origin in `SuccessfulTripResponse.json` has neither estimated nor planned time (verified).
  - One bad journey discards the other 4.
- **Failure scenario:**
  1. The cron fires, and `Notify("Starting trip timer")` runs.
  2. `GetNextDepartures` throws before `SecondChanged` is subscribed.
  3. `WakeUp` logs the exception, and the clock shows nothing for the whole ActiveTime window, with no retry. The user misses the train.
- **Fix:**
  - If `Error` is set or `Journeys` is null, log `Error.Message` and return an empty list.
  - Skip journeys with no legs or no time, with a warning.
  - Use `DepartureTimeEstimated ?? DepartureTimePlanned`, and the same for arrival.
  - Parse with `DateTimeOffset.TryParse(..., CultureInfo.InvariantCulture, ...)`.
  - Catch `TripPlannerException`/`HttpRequestException` in `TripTimerApp` (see CR-25 for retry).
- **Config compat:** No change.

### Medium

#### CR-11. TimerService callbacks overlap: duplicate or missed ticks
- **Severity:** Medium
- **Sources:** core M3; apps C1 (tick overlap) and M4 (TimerService part)
- **Refs:** `src/api/HostedServices/TimerService.cs:65` (100 ms period), `:96` (compares `.Second` only), `:102` (minute check nested inside the second check), `:109` (`_lastTime` updated after the handlers)
- **Problem:**
  - A `System.Threading.Timer` does not wait for the previous callback. While handlers block on I/O, the next callback also sees "second changed" and fires again.
  - `_lastTime` is not synchronised.
  - A stall of exactly a whole number of minutes, landing on the same second, drops both the second and minute events.
- **Failure scenario:** The broker is slow (400 ms per publish), so 3-4 overlapping callbacks send 3-4 identical `custom/TripTimerApp` publishes per second. DiurnalApp can apply the same minute twice, and thread-pool usage grows.
- **Fix:**
  - Replace the timer with a `PeriodicTimer` loop, or add an `Interlocked` re-entrancy guard.
  - Set `_lastTime` before invoking handlers.
  - Compare truncated `DateTime` values rather than the `.Second` field.
  - Inject `IClock`/`TimeProvider` (CR-35).
- **Config compat:** No change.

#### CR-12. HTTP transport builds wrong URLs for custom apps
- **Severity:** Medium
- **Source:** core M4
- **Refs:** `src/api/Services/AwtrixService.cs:36-44` (AppUpdate/AppClear), `:100-110` (scheme sniffing); `Apps/Buttons/ButtonApp.cs:48` (topic built from BaseTopic)
- **Problem:** `{BaseTopic}/custom/{appName}` is the MQTT form. The Awtrix 3 HTTP API is `POST http://<ip>/api/custom?name=<app>`. The notify, dismiss, settings and rtttl paths do line up when BaseTopic is `http://ip/api`, so only custom apps are affected. `HttpPublisher` returns `false` on 404, and no caller checks the result.
- **Failure scenario:**
  - A device with `"BaseTopic": "http://192.168.1.50/api"` running TripTimer or MqttRender gets a 404 on every update, and nothing displays.
  - ButtonApp subscribes to the MQTT topic `http://.../stats/buttonLeft`, which never fires.
- **Fix:**
  - Let the publisher map `custom/{name}` to `custom?name={name}` for HTTP.
  - Don't create ButtonApp for HTTP devices, and log that buttons are unsupported.
  - Log a warning when a publish returns `false`.
- **Config compat:** No change; the `BaseTopic` format and meaning are unchanged.

#### CR-13. Config precedence inverted: appsettings.json re-added last
- **Severity:** Medium
- **Source:** core M1
- **Refs:** `src/api/Program.cs:95-99`
- **Problem:** `CreateBuilder` already loads, in order: appsettings.json, appsettings.{Env}.json, user secrets, env vars, command line. `SetupConfiguration` then re-adds `appsettings.json` after all of them, so it overrides everything except the `AWTRIXSHARP_*` provider added after it. This also creates a second file watcher.
- **Failure scenario:** A `Awtrix:Devices:0:BaseTopic` override in user secrets, `appsettings.Development.json` or `--Awtrix:...` is silently ignored.
- **Fix:** Delete the `.AddJsonFile("appsettings.json", ...)` line and keep `.AddEnvironmentVariables("AWTRIXSHARP_")`.
- **Config compat:** Compatible.
  - All keys and env vars still resolve, and `AWTRIXSHARP_*` still wins.
  - Behaviour change: user secrets, appsettings.{Env}.json and command-line values now override appsettings.json, as .NET intends. In the repo today, `appsettings.Development.json` contains only `Logging`, so the only visible change is that Development log levels take effect.
  - Call this out in release notes.

#### CR-14. Secrets read with `Environment.GetEnvironmentVariable`, bypassing IConfiguration
- **Severity:** Medium
- **Sources:** core M6 and trip L8
- **Refs:**
  - `src/api/Program.cs:36` (TfNSW key)
  - `HostedServices/SlackConnector.cs:53` (app token)
  - `Apps/Configs/AppConfigKeys.cs:29` (env fallback, e.g. `SlackUserId`)
  - `Services/TripPlanner/TripPlannerService.cs:124` (`DATA_DIRECTORY`)
  - `Debug/SlackUserHarvester.cs:16,58`
- **Problem:** These values can only come from real environment variables. User secrets and appsettings are ignored, which contradicts CLAUDE.md. A missing TfNSW key becomes `"apikey "` with no startup warning.
- **Failure scenario:**
  - A developer puts the Slack token in user secrets → "Slack integration disabled", with no explanation.
  - The TfNSW key is in user secrets → startup is clean, then a 401 at 06:00.
- **Fix:**
  - Read `Slack:AppToken`, `Slack:UserId`, `Settings:DATA_DIRECTORY` and `TransportOpenData:ApiKey` from `IConfiguration`.
  - Bind with `Configure<TransportOpenDataConfig>(GetSection("TransportOpenData"))`.
  - Keep a fallback to the literal env var name.
  - Warn at startup if a TripTimerApp is configured but the key is empty.
- **Config compat:** Compatible. `AWTRIXSHARP_SLACK__APPTOKEN` maps to `Slack:AppToken` through the prefixed provider, and `TRANSPORTOPENDATA__APIKEY` maps to `TransportOpenData:ApiKey` through the default provider. The fallback covers any edge cases.

#### CR-15. DeveloperExceptionPage always on; bad input returns 500 with a stack trace
- **Severity:** Medium
- **Sources:** core M5 and trip L11 (the auth and Swagger-default parts are Deferred)
- **Refs:**
  - `src/api/Program.cs:80-86`
  - `Controllers/TripTimerController.cs:36` (`DateTimeOffset.Parse`), `:38-40` (`.First()`)
  - `Controllers/TripPlannerController.cs:51,78` (culture-dependent `DateTime.Parse`; the catch-all returns 500)
  - CR-07 for success-when-nothing-happened
- **Problem:** The environment guard is commented out, so any unhandled exception returns a full stack trace to any LAN client. Invalid or missing input produces a 500 instead of a 400 or 404.
- **Failure scenario:** `POST /api/app/TripTimer/test/alarm-timings` with no TripTimerApp configured returns a 500 with a stack trace.
- **Fix:**
  - Use `UseDeveloperExceptionPage()` only when `IsDevelopment()`; otherwise use `UseExceptionHandler` with ProblemDetails.
  - Bind `DateTimeOffset` query params or `TryParse` them (invariant culture), returning `BadRequest`.
  - Use `FirstOrDefault` and return `NotFound`.
  - Add an optional `Swagger:Enabled` setting (default `true`).
  - Add an optional `Api:Key` check that is skipped when unset.
- **Config compat:** Compatible; the new keys are optional and default to current behaviour.

#### CR-16. `SlackConnector.StopAsync` throws NRE when Slack is not configured
- **Severity:** Medium
- **Source:** core M2
- **Refs:** `src/api/HostedServices/SlackConnector.cs:131` (plus `:24` static field)
- **Problem:** With no app token, `ExecuteAsync` returns early and `_slackSocketClient` stays null. `_executingTask` is non-null, so `Disconnect()` throws an NRE.
- **Failure scenario:** Every container stop without Slack configured logs "Hosting failed to stop" and exits non-zero.
- **Fix:** `_slackSocketClient?.Disconnect()`. See also CR-41.
- **Config compat:** No change.

#### CR-17. CI never runs tests
- **Severity:** Medium
- **Source:** core M7
- **Refs:** `.github/workflows/docker-publish.yml` has no `dotnet` step. The `Dockerfile` builds only `src/api`.
- **Problem:** A PR can break all 43 tests and still pass CI, and master still publishes an image.
- **Fix:** Add a `test` job (`actions/setup-dotnet` 10.0.x, `dotnet test`) that the docker job `needs:`.
- **Config compat:** No change.

#### CR-18. `ExecuteNow` runs have no ActiveTime limit
- **Severity:** Medium
- **Source:** apps M1
- **Refs:** `src/api/Apps/ScheduledApp.cs:120-126`. `CancelAfter(Config.ActiveTime)` is applied only at `:115`.
- **Problem:** A manual run never times out, and it cancels the pending cron waiter. Nothing reschedules until the manual run ends.
- **Failure scenario:** After `POST /api/app/MqttRender/start` the value stays on the clock until restart, and the next 08:00 cron never fires.
- **Fix:** Apply `CancelAfter(ActiveTime)` in `WakeUp` for both paths. Implement together with CR-08.
- **Config compat:** No change.

#### CR-19. TripTimer "no departures" publishes `{}` and cancels via the shared `_cts` field
- **Severity:** Medium
- **Source:** apps M2
- **Refs:** `src/api/Apps/TripTimer/TripTimerApp.cs:85-89`, then `:74`
- **Problem:**
  - `BuildMessage` calls `_cts.Cancel()`, whose continuation runs `Deactivate` → `AppClear` inline (CR-08).
  - It then returns an empty message, and `ClockTickSecond` publishes `{}`. That payload is not empty, so Awtrix does not treat it as a delete.
  - The order of the clear and the `{}` publish is not guaranteed.
  - `_cts` may already have been disposed → `ObjectDisposedException` → CR-01.
- **Failure scenario:** After the last departure, a blank "TripTimerApp" page stays in the clock's rotation until the next morning.
- **Fix:** Return `null` when there are no departures and skip the publish. Signal completion through the per-activation CTS (CR-08). Consider re-querying before cancelling (CR-25).
- **Config compat:** No change.

#### CR-20. Diurnal: startup replay ignores yesterday; a missed minute skips that setting
- **Severity:** Medium
- **Sources:** apps M3 and M4
- **Refs:** `src/api/Apps/Diurnal/DiurnalApp.cs:92-108` (replay only `t < now`, today), `:114-116` (exact `ContainsKey(currentTime)` match)
- **Problem:**
  - The replay skips entries still in effect from yesterday evening.
  - A tick must land on exactly that minute. A stall, a CR-11 drop, host suspend or the DST gap skips the setting for the whole day.
- **Failure scenarios** (shipped config: 0600 Brightness=8, 0700 TCOL white, 1900 TCOL red, 2100 Brightness=1):
  - Restart at 03:00: nothing is replayed, and the clock can stay at brightness 8 until 06:00.
  - Restart at 06:30: text colour is not restored to red.
  - A stall from 20:59:50 to 21:01:05: the clock stays bright all night.
- **Fix:**
  - Sort the keys, then replay in chronological order starting after `now`: yesterday's remaining entries, then today's up to `now`. Only the final value of each setting needs sending.
  - At runtime, track the last processed time and apply every entry in (last, now].
  - Inject `IClock` (removing the `DateTime.Now` calls at `:92,104`).
- **Config compat:** No change to config; startup behaviour now matches intent.

#### CR-21. Diurnal setting values validated lazily; empty settings crash `ToString`
- **Severity:** Medium
- **Source:** apps M5 plus the core C1 trigger
- **Refs:** `src/api/Apps/Diurnal/DiurnalApp.cs:47-87` (the try/catch only wraps lambda construction), `:70` (`byte.Parse` inside the lambda); `Domain/AwtrixSettings.cs:25-27`
- **Problem:**
  - `"2100": "Brightness=dim"` fails in the startup replay (host fails to start) or at 21:00 (process crash, CR-01).
  - An unknown setting name leaves an empty action list, which crashes `ToString()`.
- **Fix:**
  - Parse values when building the map with `byte.TryParse(..., CultureInfo.InvariantCulture)`.
  - Log and skip invalid values or unknown keys.
  - Drop empty time entries.
  - Make `ToString` safe on an empty dictionary (shared with CR-01).
- **Config compat:** Compatible. Previously fatal invalid entries are now logged and skipped.

#### CR-22. App config and ValueMap key lookup is case-sensitive
- **Severity:** Medium
- **Source:** apps M6
- **Refs:** `src/api/Apps/Configs/AppConfigKeys.cs:5`, `Apps/Configs/ValueMap.cs:7` (plain `Dictionary<string,string>`, ordinal comparer)
- **Problem:** `IConfiguration` keys are case-insensitive, and the env-var provider keeps the variable's casing.
- **Failure scenarios:**
  - `AWTRIXSHARP_AWTRIX__DEVICES__0__APPS__1__CONFIG__STOPIDORIGIN` is ignored.
  - A hand-written `"cronSchedule"` → `CrontabSchedule.Parse(null)` → startup fails.
  - `"valueMatcher"` never matches.
- **Fix:** Construct both classes with `StringComparer.OrdinalIgnoreCase`, and make sure `Clone()` keeps the comparer.
- **Config compat:** Compatible. Correctly-cased keys still work. The only theoretical break is two keys that differ only by case, which is unsupported by `IConfiguration` anyway.

#### CR-23. Typed config values parsed late with current culture; a failure permanently stops the app
- **Severity:** Medium
- **Sources:** apps M7; core and apps CONCERNS verification
- **Refs:**
  - `src/api/Apps/Configs/AppConfig.cs:60` (`(T)ConvertValue(...)`), `:122-155` (culture-sensitive `Parse`)
  - read sites: `Apps/ScheduledApp.cs:31,115`; `Apps/TripTimer/TripTimerApp.cs:178-179,196`
- **Problem:**
  - A missing value-type key → `(T)null` → NRE.
  - A malformed value → `FormatException`.
  - Parsing happens on property access, and `ActiveTime` is first read after the cron delay.
  - Parsing uses the current culture.
- **Failure scenario:** A TripTimer config without `ActiveTime` starts cleanly. At 06:10 the waiter throws, and `ScheduledApp.cs:92-96` logs "Error in TripTimerApp: Object reference…". The app never reschedules (CR-08).
- **Fix:**
  - Add `Validate()` on the config classes: required keys, `TryParse` with `CultureInfo.InvariantCulture`, and an error that names the device, app and key.
  - Call it from Conductor before `Init`; a failing app is skipped (CR-05/CR-30).
  - Return `default(T)` for missing optional values.
- **Config compat:** Compatible for valid configs. Invariant-culture parsing matches the format of the shipped values (`00:30:00`). A config that relied on a non-invariant culture format would change, which is unlikely; mention it in release notes.

#### CR-24. SlackStatusApp NRE on every user_change when no user id is configured
- **Severity:** Medium
- **Source:** apps M8
- **Refs:** `src/api/Apps/SlackStatus/SlackStatusApp.cs:28,34` (`_trackingUserId.Equals` on null), `:38` (`== string.Empty` misses null), `:41,59` (`.Result` on SlackNet's dispatch thread)
- **Problem and failure scenario:** The shipped appsettings has `"SlackUserId": ""`. With a token set and no `AWTRIXSHARP_SLACK__USERID`, every status change by anyone in the workspace logs a stack trace. A null status publishes `text=null`.
- **Fix:**
  - Use `string.Equals(..., Ordinal)`.
  - If the id is empty, warn once and don't subscribe.
  - Use `string.IsNullOrEmpty(e.StatusText)`.
  - Use an async handler with try/catch.
- **Config compat:** No change.

#### CR-25. Departures fetched once per activation: no refresh, no retry
- **Severity:** Medium
- **Source:** trip M2
- **Refs:** `src/api/Apps/TripTimer/TripTimerApp.cs:196-206`, `:85-89`
- **Problem:**
  - The departure list is a snapshot taken at activation and used for the whole ActiveTime window (30+ minutes).
  - Delays that appear later are never picked up.
  - A transient 503 or timeout loses the whole window.
  - When the list runs out, the app cancels instead of re-querying.
- **Failure scenario:** At activation the 06:41 shows on time. At 06:10 it is marked 8 minutes late, and the clock still alarms for 06:41.
- **Fix:**
  - Re-query every N minutes on `MinuteChanged`, guarding against an in-flight request.
  - On failure, keep the last good list and retry with backoff.
  - Re-query when the list is exhausted.
- **Config compat:** Compatible. Hard-code the interval (e.g. 2 min) or add an optional `RefreshInterval` key with a default.

#### CR-26. Trip query and schedules depend on host timezone
- **Severity:** Medium
- **Sources:** trip M3 and apps L3 (TZ part)
- **Refs:**
  - `src/api/Apps/TripTimer/TripTimerApp.cs:196` (`earliestDeparture.LocalDateTime`)
  - `Services/TripPlanner/TripPlannerService.cs:48-49` (`itdDate`/`itdTime` from a `DateTime`), `:95` (`ToLocal`)
  - `Controllers/TripPlannerController.cs:51,78`
  - `Apps/ScheduledApp.cs:103-104`, `Apps/Diurnal/DiurnalApp.cs:92`
  - `Dockerfile` has no `TZ`; only `readme.md:228,239` sets `TZ: "Australia/Sydney"`
- **Problem:** TfNSW reads `itdDate`/`itdTime` as Sydney local time, but the code formats host-local time and drops the offset.
- **Failure scenario:**
  1. The container runs without `TZ` (UTC).
  2. At 06:00 AEST the app asks for trips after 20:22 on the previous date and gets the previous evening's trips.
  3. They are all in the past, so "No future departures" → the trip timer never works, and nothing reports an error.
- **Fix:**
  - Change `GetTrips`/`GetNextDepartures` to take `DateTimeOffset`.
  - Convert with `TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney")`, and format with `InvariantCulture`.
  - Add an optional `TransportOpenData:TimeZone` setting (default `Australia/Sydney`; the API is NSW-only).
  - For cron and Diurnal, keep host-local as the default. Document `TZ`, and add `ENV TZ` guidance to the readme rather than baking a zone into the image.
- **Config compat:** Compatible.
  - Hosts with `TZ=Australia/Sydney` behave identically.
  - UTC hosts get correct trip queries (bug fix).
  - Cron and Diurnal semantics are unchanged.
  - `ITripPlannerService` signature changes (internal; update the test mock).

#### CR-27. Leading walking leg treated as the train departure
- **Severity:** Medium
- **Source:** trip M4
- **Refs:** `src/api/Services/TripPlanner/TripPlannerService.cs:100-106`; `ComplexTripResponse.json`, journeys 2 and 6 (1-based)
- **Problem:** `Legs.First()` is used as the departure. Journeys 2 and 6 in the fixture start with a class-100 footpath leg (verified), for 05:41:30 and 06:04, then board the same services that other journeys board directly (05:53 and 06:13).
- **Failure scenario:** The walk time is double-counted against `TimeToOrigin`. The user gets an early "GO!" for a departure that does not exist, plus a duplicate alarm for the real train.
- **Fix:**
  - Use the first leg whose `Transportation.Product.Class` is not 99 or 100 as the origin.
  - De-duplicate by origin departure time.
  - Optionally skip journeys whose first transit stop is not `StopIdOrigin`, logged only, not a config switch.
- **Config compat:** No change.

#### CR-28. Cancelled services not detected
- **Severity:** Medium
- **Source:** trip M5
- **Refs:** `src/transportOpenData/TripPlanner/TripPlannerClient.nswag.cs` (`TripRequestResponseJourneyLeg`, around `:3085`); `TripPlannerService.cs:98-115`
- **Problem:** `realtimeStatus` and `isRealtimeControlled` are present in the fixtures (`["MONITORED"]`), but they are not modelled, so they are dropped, and nothing filters cancellations.
- **Caveat:** the `CANCELLED` value comes from TfNSW documentation, not repo data. Confirm it before implementing.
- **Failure scenario:** A cancelled 06:41 is still returned, and the clock counts down to it.
- **Fix:** Add a hand-written `partial class TripRequestResponseJourneyLeg { [JsonPropertyName("realtimeStatus")] ICollection<string>? RealtimeStatus }`. Skip journeys where any transit leg has `CANCELLED`.
- **Config compat:** No change.

#### CR-29. Typed HttpClients captured by singleton; no timeout, retry or cancellation
- **Severity:** Medium
- **Sources:** trip M6 and core L2
- **Refs:**
  - `src/api/Program.cs:43,53-67`
  - `HostedServices/Conductor.cs:63,160`
  - `Services/TripPlanner/TripPlannerService.cs:44` (`Request2Async` without a token)
- **Problem:** The transient `TripPlannerService` holds typed `TripClient`/`StopfinderClient`. It is captured by the singleton `Conductor` for the life of the process, so handler rotation and DNS refresh never happen. The timeout is the default 100 s, there is no resilience handler, and deactivation or shutdown cannot cancel an in-flight request.
- **Failure scenario:** After weeks of uptime, the CDN IP for `api.transport.nsw.gov.au` changes, and requests hang until the 100 s timeout, eating into the active window.
- **Fix:**
  - Make `TripPlannerService` a singleton over `IHttpClientFactory`, creating the NSwag clients per call. Alternatively set `PooledConnectionLifetime` to 5 min.
  - Set a 15 s timeout and add `AddStandardResilienceHandler()`.
  - Thread a `CancellationToken` from the activation CTS down to `Request2Async`.
- **Config compat:** No change.

### Low

#### CR-30. Unknown app `Type` / missing CronSchedule crashes host with unhelpful error
- **Severity:** Low
- **Source:** core L3
- **Refs:** `src/api/HostedServices/Conductor.cs:196-197` (`NotImplementedException(appConfig.Type)`); `Apps/ScheduledApp.cs:31` (`CrontabSchedule.Parse(null)`)
- **Problem:** A typo such as `"MqttRendrApp"` stops every device from starting, with a cryptic error.
- **Fix:** Log `Unknown app type 'X' on device 'Y'` and skip that app, together with CR-05 and CR-23.
- **Config compat:** Compatible. Previously fatal typos become a logged skip.

#### CR-31. Dispose pattern broken
- **Severity:** Low
- **Sources:** apps L1; core out-of-scope note
- **Refs:**
  - `src/api/Apps/TripTimer/TripTimerApp.cs:238-252` (`new` hides; never called via `IAwtrixApp`)
  - `Apps/ScheduledApp.cs:38-55` (fire-and-forget `Dismiss`/`AppClear`; non-virtual `Dispose(bool)`)
  - `Apps/AwtrixApp.cs:78-81` (`.Wait()`, hidden for scheduled apps)
  - `HostedServices/Conductor.cs:241`
- **Problem:**
  - TripTimer's unsubscribe code is dead.
  - The clears race MQTT disconnect at shutdown.
  - `Dismiss` also removes notifications posted by other apps.
  - Diurnal, Button and Slack apps never unsubscribe from their events.
  - A throwing Dispose aborts the rest of the loop.
- **Fix:**
  - Implement a single `protected virtual Dispose(bool)`, or `IAsyncDisposable`, in `AwtrixApp`, with overrides that unsubscribe.
  - Await clears with a bounded timeout.
  - Wrap each app's dispose in try/catch in Conductor.
  - Make `Conductor.StopAsync` await it before `MqttConnector` stops. Hosted services stop in reverse registration order, so Conductor stops before MqttConnector.
- **Config compat:** No change.

#### CR-32. DoubleClickDetector uses `DateTime.Now`; double-click also fires Click
- **Severity:** Low
- **Source:** apps L2
- **Refs:** `src/api/Apps/Buttons/DoubleClickDetector.cs:17`; `Apps/Buttons/ButtonState.cs:26-37`
- **Problem:** When DST ends, `now - last` goes negative and is treated as a double-click. The first press of every double-click is also raised as a Click.
- **Fix:** Use `Stopwatch.GetTimestamp()`/`TimeProvider`. Making Click and DoubleClick mutually exclusive is under Deferred.
- **Config compat:** No change.

#### CR-33. MqttRenderApp attaches handler after subscribe
- **Severity:** Low
- **Source:** apps L4
- **Refs:** `src/api/Apps/MqttRender/MqttRenderApp.cs:34-35`
- **Problem:** A retained message delivered right after SUBACK can arrive before `+=` runs, so the clock stays blank until the next publish.
- **Fix:** Attach the handler first, then subscribe. Folded into the WS2 handler registry.
- **Config compat:** No change.

#### CR-34. ValueMap: some setters silently ignored; arrays serialised as strings; invalid regex silent
- **Severity:** Low
- **Sources:** apps L5; core L5 (culture)
- **Refs:**
  - `src/api/Apps/Configs/ValueMap.cs:35-39` (invalid regex silently falls back to `Contains`), `:124` and `:138-162` (reflection setter lookup)
  - `Domain/AwtrixAppMessage.cs:83,89` (culture-sensitive `double.ToString()`), `:129-177` (array setters)
- **Problem:**
  - `BlinkText` and `FadeText` (double) and `Gradient` (`int[][]`) ValueMap keys do nothing, and nothing is logged.
  - `bar`, `line` and `gradient` are emitted as the JSON string `"1,2,3"`, but Awtrix expects arrays.
  - A comma-decimal culture sends `"0,5"`.
- **Fix:**
  - Build a static setter table.
  - Emit arrays as JSON arrays in `ToJson`.
  - Use `InvariantCulture`.
  - Log unknown keys and invalid regexes once, at load.
- **Config compat:** Compatible. Keys that were silently ignored now take effect.

#### CR-35. Testability seams missing
- **Severity:** Low
- **Sources:** core L6; apps L3 and L6
- **Refs:**
  - `src/api/HostedServices/Conductor.cs:137-138` (news up `AwtrixService` and `Clock`) and concrete ctor dependencies
  - `Services/MqttPublisher.cs:9-11` (concrete `MqttConnector`)
  - `Apps/SlackStatus/SlackStatusApp.cs` (concrete `AwtrixService`, `SlackConnector`)
  - `HostedServices/TimerService.cs:90` and `Apps/Diurnal/DiurnalApp.cs:92,104` (`DateTime.Now`)
- **Problem:** CR-03, 05-09, 11, 16 and 20 cannot be unit-tested without a broker or real time.
- **Fix:**
  - Depend on the existing `IMqttConnector`, `ITimerService` and `IAwtrixService`.
  - Add `ISlackConnector`.
  - Inject `IClock`/`TimeProvider` into `TimerService` and `DiurnalApp`.
- **Config compat:** No change.

#### CR-36. `TransportOpenData:BaseUrl` is dead config
- **Severity:** Low
- **Source:** trip L7
- **Refs:** `src/api/Program.cs:37` (default `/v1/tp`); `src/transportOpenData/TransportOpenDataConfig.cs:10` (default `/v1`); the NSwag ctor hard-codes its base URL
- **Problem:** The value is read but never applied, so a proxy or mock URL is silently ignored.
- **Fix:** Set the client's `BaseUrl` from options in the factory, and align the default to `/v1/tp`.
- **Config compat:** Compatible. The key starts taking effect; anyone who set it to a wrong value would see a change.

#### CR-37. Trip file cache correctness
- **Severity:** Low
- **Source:** trip L9
- **Refs:** `src/api/Services/TripPlanner/TripPlannerService.cs:80-85,120-164` (`:128` filename, `:134` deserialize, `:136` `DateTimeOffset.Now`); `Controllers/TripPlannerController.cs:57` (the writer is commented out)
- **Problem:** This is an opt-in feature, enabled via `AWTRIXSHARP_SETTINGS__DATA_DIRECTORY`.
  - A cache hit replaces live data.
  - Post-midnight entries are re-dated into the past.
  - A `null` or invalid JSON file throws instead of falling back to the API.
  - `originId`/`destinationId` from the query go unsanitised into the filename.
  - No supported writer exists.
- **Fix:**
  - Wrap the cache read in try/catch and fall back to the API.
  - Null-check the result.
  - Roll re-dated times forward a day when needed.
  - Accept digits-only stop IDs.
  - Use `IClock`.
- **Config compat:** No change. The feature is kept; removing it is under Deferred.

#### CR-38. Strict enum converters in generated model
- **Severity:** Low
- **Source:** trip L14
- **Refs:** `src/transportOpenData/TripPlanner/TripPlannerClient.nswag.cs:3421` (leg-stop `Type`), `:4651`, `:5336`
- **Problem:** The leg-stop `type` enum is closed and lacks values such as `gisPoint`, which the sibling `DestinationType` allows. An unknown value throws a `JsonException`, the client wraps it as a `TripPlannerException`, and the whole request fails.
- **Fix:** Use a lenient enum converter that falls back to `unknown`, applied in a partial `UpdateJsonSerializerSettings`, or map these properties to `string`.
- **Config compat:** No change.

#### CR-39. Test gaps: apps and scheduler
- **Severity:** Low
- **Source:** apps L7
- **Refs:** `test/Test/Configs/ValueMapsTests.cs` (entirely commented out; references a removed `ValueMapJsonConverter`); `test/Test/Apps/TripTimerAppTests.cs` (`GetSystemUnderTest` uses `DateTimeOffset.Now`)
- **Problem:** There are no tests for:
  - `ValueMap.IsMatch`/`Decorate`
  - the ScheduledApp lifecycle (CR-08, CR-18)
  - MqttClockRenderApp (CR-09)
  - the no-departures branch (CR-19)
- **Fix:**
  - Restore the ValueMap tests.
  - Add lifecycle tests with a fake clock.
  - Pin test clocks to fixed instants.
- **Config compat:** No change.

#### CR-40. Test gaps: trip planner
- **Severity:** Low
- **Source:** trip L13
- **Refs:** `test/transportOpenData.Tests/TripPlanner/TripRequestResponseDeserializationTests.cs:14-27`; `test/transportOpenData.Tests/Helpers/TestDataHelper.cs:14-21`
- **Problem:** There are no `TripPlannerService` tests. The fixture tests use case-insensitive camelCase options, while production uses the client's default options, so the tests can pass while production fails.
- **Fix:**
  - Deserialize fixtures through a real `TripClient` over a stub `HttpMessageHandler`.
  - Add `GetNextDepartures` tests for: `ErrorResponse.json` (CR-10), a missing estimate (CR-10), the walking leg (CR-27), and a UTC host (CR-26).
- **Config compat:** No change.

#### CR-41. SlackConnector static client fields and dead members
- **Severity:** Low
- **Source:** core L1
- **Refs:** `src/api/HostedServices/SlackConnector.cs:2` (unused `Newtonsoft.Json.Linq`), `:23-24` (static fields), `:28` (`UserDnChanged` never raised)
- **Problem:** Static fields are shared across instances, `_slackApiClient` is never assigned, and after the initial connect the service relies entirely on SlackNet's own reconnect.
- **Fix:** Make the fields instance fields, delete the dead members, and document that DND is unsupported.
- **Config compat:** No change.

#### CR-42. Misc Conductor/Domain defects
- **Severity:** Low
- **Source:** core L5
- **Refs and fixes:**
  - `src/api/HostedServices/Conductor.cs:166`: ButtonApp uses a `MqttRenderApp` logger category. Use a ButtonApp logger.
  - `Domain/AwtrixAppMessage.cs:216`: `"Text"` should be `"text"`, so the name-first ordering never applies. Compare against `"text"`.
  - `Services/AwtrixService.cs:77`: `else if (p < 4)` is unreachable. Remove it.
- **Config compat:** No change.

#### CR-43. Dead or misleading code and lossy error logging
- **Severity:** Low
- **Sources:** apps L6 and trip L12; CONCERNS confirmations
- **Refs and fixes:**
  - `Apps/MqttRender/MqttRenderApp.cs:89-92`: unreachable `Text == null` check. Delete.
  - `Apps/AwtrixApp.cs:66-70`: dead `Config.Name == null` "Diurnal" guard. Delete.
  - `SlackStatusAppConfig.SlackUserId`: never populated. Delete.
  - `Apps/TripTimer/TripTimerApp.cs:128`: inverted comment. Correct it.
  - `TripTimerApp.cs:207`: unused `departuresCsv`. Delete.
  - `Services/TripPlanner/TripPlannerService.cs:4`: unused `Newtonsoft.Json.Linq`. Delete.
  - `Controllers/TripPlannerController.cs:54-57`: commented-out `D:\downloads` block. Delete.
  - `Apps/ScheduledApp.cs:95`: hard-coded "Error in TripTimerApp", message only. Use `LogError(ex, "... {App}", Config.Name)`.
  - `ScheduledApp.cs:140`: `LogError($"...{ex}", ex)` passes the exception as a template argument. Same fix.
  - `TripTimerApp.cs:222` and `MqttRenderApp.cs:43`: log `ex.Message` only. Log the exception, and include `TripPlannerException.StatusCode`.
- **Config compat:** No change.

#### CR-44. `TripSummary.ToString` wrong duration for trips of an hour or more
- **Severity:** Low
- **Source:** trip L10
- **Refs:** `src/api/Services/TripPlanner/TripSummary.cs:22`
- **Problem:** `{TravelTime:mm}` shows 75 min as "15 mins". Fixture trips are 66-73 min, so this happens with real data.
- **Fix:** `{(int)TravelTime.TotalMinutes} mins`.
- **Config compat:** No change.

#### CR-45. Dockerfile restore layer ineffective; `EXPOSE 8081`, HTTPS redirection, dead SlackUserHarvester
- **Severity:** Low
- **Source:** core L7
- **Refs:**
  - `Dockerfile:16-17`: only `awtrix-api.csproj` is copied before restore; the `transportOpenData` csproj is missing
  - `Dockerfile:20,28`: publish rebuilds after build
  - `Dockerfile:10`: `EXPOSE 8081` with nothing listening
  - `src/api/Program.cs:88`: `UseHttpsRedirection` is a no-op (HTTP only) and logs a warning
  - `src/api/Debug/SlackUserHarvester.cs`: unreferenced, and calls `Environment.Exit`
- **Fix:**
  - Copy both csproj files before restore.
  - Use a single `dotnet publish --no-restore`.
  - Drop `EXPOSE 8081`, the HTTPS redirection and the harvester, or put the harvester behind a CLI flag.
- **Config compat:** Compatible; ports and env vars are unchanged.

---

## Workstreams

Ordered by priority and dependency. Each is sized for one spec plus implementation plan.

### WS1. Runtime resilience: timer, publish path, test seams
**IDs:** CR-01, CR-02, CR-11, CR-12, CR-35
**Depends on:** none. CR-17 from WS8 should land first so CI guards the work.

**Approach:**
- Establish two contracts every later workstream relies on:
  - Clock-tick handlers can never take down the process.
  - Publishers never throw and return an honest `bool`.
- **TimerService:** rewrite as a non-overlapping `PeriodicTimer` loop over an injected `TimeProvider`/`IClock`. Invoke each subscriber through `GetInvocationList()` inside its own try/catch, and compare truncated `DateTime` values.
- **HttpPublisher:** move to `IHttpClientFactory` with a 5 s timeout, disposed responses, and exceptions caught and logged.
- **Transport paths:** move path construction into the publishers so HTTP custom apps use `custom?name=`. Warn on a `false` result.
- **Handlers:** convert `.Result`/`.Wait()` in timer handlers to an async-safe pattern: a helper such as `FireAndLog(Func<Task>)` on `AwtrixApp`.
- **Seams:** swap concrete constructor dependencies for the existing interfaces so WS2-WS5 are unit-testable.

### WS2. MQTT connector: single client, reconnect, subscription registry
**IDs:** CR-03, CR-04, CR-33
**Depends on:** WS1 (publish-result contract, `IMqttConnector` seam).

**Approach:**
- Rebuild `MqttConnector` around one long-lived `IMqttClient`, created in the constructor:
  - `DisconnectedAsync` → background reconnect loop with exponential backoff, guarded by a `SemaphoreSlim`.
  - A topic registry replayed on every `ConnectedAsync`.
  - `Subscribe` records the topic and applies it when connected, never throwing while disconnected.
  - `PublishAsync` returns `bool`.
  - Stop disposes the client.
- Handler registration becomes independent of client identity, which also fixes the attach-order race in CR-33.
- Remove the `ConnectAsync` call from `MqttController`.
- Add tests with a fake `IMqttClient`: broker restart → handlers and subscriptions survive.

### WS3. Conductor and hosted-service lifecycle
**IDs:** CR-05, CR-06, CR-07, CR-16, CR-30
**Depends on:** WS1, WS2 (start no longer blocks or throws when the broker or device is down).

**Approach:**
- **Init:** introduce `IAwtrixApp.InitAsync()` (the net10 branch memory notes this rename was done elsewhere; re-apply it here). Initialise each app exactly once, after all apps are created, and guard against double init.
- **Start:** per-app try/catch that logs the device, app type and reason and continues. Unknown types are logged and skipped.
- **Registry:** track apps by (device BaseTopic, type). `ExecuteNow` targets the existing instance and returns NotFound/Started/Error, mapped to 404/200/500 by `TripTimerController` and `MqttRenderController`.
- **Stop:** `StopAsync` awaits each app's (async) dispose in try/catch. Fix the `SlackConnector.StopAsync` null client.
- Config validation hooks from WS7 (CR-23) plug into the same per-app guard.

### WS4. ScheduledApp engine and scheduled apps
**IDs:** CR-08, CR-09, CR-18, CR-19, CR-31, CR-39
**Depends on:** WS1 (tick contract), WS3 (single init, disposal ordering).

**Approach:**
- **ScheduledApp state machine:**
  - An app-lifetime CTS.
  - A per-activation linked CTS carrying `CancelAfter(ActiveTime)` for both cron and manual runs.
  - Lock-protected transitions and a `_disposed` flag.
  - `RunContinuationsAsynchronously` waits.
  - Chunked delays over 49.7 days.
  - Guaranteed reschedule after any failure.
- **Hooks:** add `protected virtual` activate and deactivate hooks.
  - MqttClockRenderApp unsubscribes its tick in the deactivate hook.
  - TripTimerApp returns null instead of `{}` and completes through the activation token.
- **Dispose:** one virtual (async) dispose pattern across `AwtrixApp`/`ScheduledApp`/`TripTimerApp`.
- **Tests:** restore the ValueMap tests and add lifecycle tests on a fake clock, covering ExecuteNow during an active run, double ExecuteNow, and shutdown mid-run.

### WS5. Event-driven apps and display correctness (Diurnal, Slack, Button, ValueMap)
**IDs:** CR-20, CR-21, CR-24, CR-32, CR-34
**Depends on:** WS1 (IClock, tick contract). Independent of WS4, so it can run in parallel.

**Approach:**
- **DiurnalApp:**
  - Parse and validate the time map eagerly with the invariant culture, logging and skipping bad entries.
  - Replace exact-minute matching with "apply all entries in (last, now]".
  - Replace the startup replay with a rotation that includes yesterday's remaining entries.
  - Make `AwtrixSettings.ToString` safe.
- **SlackStatusApp:** null-safe user-id and status handling, and an async handler.
- **DoubleClickDetector:** monotonic time.
- **ValueMap:**
  - A static setter table covering double and array setters.
  - JSON arrays for bar/line/gradient.
  - Invariant-culture formatting.
  - One-time warnings for unknown keys and invalid regexes.

### WS6. Trip planner robustness and timezone
**IDs:** CR-10, CR-25, CR-26, CR-27, CR-28, CR-29, CR-36, CR-37, CR-38, CR-40
**Depends on:** WS4 for CR-25 (refresh uses the per-activation token and CR-19's completion path). CR-10 and CR-27 can ship before WS4.

**Approach:**
- **`GetNextDepartures`:** harden it to take a `DateTimeOffset`. Converting to Sydney time happens inside the service.
  - Tolerate error bodies and null journeys, legs or times.
  - Use Estimated, falling back to Planned.
  - Skip footpath legs, de-duplicate, and skip cancelled services (after confirming the `CANCELLED` value).
- **HTTP clients:** re-plumb them with `IHttpClientFactory` inside a singleton service: honour `BaseUrl`, set a timeout, add the standard resilience handler, and pass a `CancellationToken`.
- **TripTimerApp:** refresh departures periodically, keeping the last good list on failure.
- **Local cache:** harden it with fallback to the API, midnight rollover and ID sanitisation.
- **Generated client:** use lenient enum handling in a partial class.
- **Tests:** add service-level tests that run the existing fixtures through a real `TripClient` over a stub handler.

### WS7. Configuration, secrets and HTTP-surface hardening (compatible subset)
**IDs:** CR-13, CR-14, CR-15, CR-22, CR-23
**Depends on:** WS3 for CR-23 (validation runs inside Conductor's per-app guard). The rest is independent.

**Approach:**
- **Precedence:** remove the duplicate `appsettings.json` source and add a release note on the corrected precedence.
- **Secrets:**
  - Read the Slack, TfNSW and DATA_DIRECTORY settings through `IConfiguration`, falling back to the literal env var names.
  - Bind `TransportOpenDataConfig` from its section.
  - Warn at startup on a missing key.
- **Config keys:** case-insensitive dictionaries for `AppConfigKeys`/`ValueMap`.
- **Typed config:** `Validate()` methods with invariant-culture `TryParse` and errors that name the key.
- **HTTP surface:**
  - Put `UseDeveloperExceptionPage` behind `IsDevelopment()`, with ProblemDetails otherwise.
  - Controller input validation returning 400/404.
  - An optional `Swagger:Enabled` (default true).
  - An optional `Api:Key` check that is inactive when unset.
- Every new key is optional and defaults to current behaviour.

### WS8. Build, CI and cleanup
**IDs:** CR-17, CR-41, CR-42, CR-43, CR-44, CR-45
**Depends on:** none. **Land CR-17 first, before WS1.** Do the remaining items after WS1-WS6 to avoid merge churn, or opportunistically inside the workstream that touches each file.

**Approach:**
- Add a `dotnet test` job gating the Docker build.
- Fix Dockerfile restore caching and the redundant build.
- Drop `EXPOSE 8081`, HTTPS redirection and SlackUserHarvester.
- Make SlackConnector fields instance fields and remove dead members.
- Fix the ButtonApp logger category, the `"text"` ordering key and the unreachable Quantize branch.
- Remove dead code and unused usings.
- Convert interpolated log calls to structured `LogError(ex, template, args)`.
- Fix the TripSummary duration format.

---

## Deferred / needs owner decision

These items are not assigned to a workstream because they break config or change behaviour clients depend on.

| Item | Why deferred |
|---|---|
| **Mandatory API authentication** on all mutating endpoints (`/Mqtt/publish` open relay using the service's broker credentials, `/api/app/*/start`, Diagnostics) | Existing callers (scripts, openHAB rules, Swagger users) send no credentials, so a mandatory key breaks them. WS7 adds an *optional* key; making it required, or binding to localhost, is an owner decision. |
| **Remove `/Mqtt/publish`, or restrict it to Development** | It is an open MQTT relay (core H2), but removing or gating it breaks any production use of the endpoint. CR-04 fixes only the client-destruction bug. |
| **Swagger UI off by default in Production** | Changes today's always-on behaviour, which the code comment says is intentional. WS7 adds the toggle with default `true`. |
| **Distinct app `Name` (two apps of the same Type on one clock)** | `AppConfig.Name => Type` (`Apps/Configs/AppConfig.cs:22`) makes both apps publish to the same `custom/{Type}` slot. An optional `Name` defaulting to `Type` is compatible, but it is a feature, and changing existing slot names would break the user's Awtrix app ordering. |
| **Click and DoubleClick mutually exclusive** (delay Click by the double-click threshold) | Changes button semantics and latency for existing Click consumers (currently logging only, but user-visible). |
| **Remove the trip file cache feature** (the alternative to hardening in CR-37) | Removing it drops support for `AWTRIXSHARP_SETTINGS__DATA_DIRECTORY` behaviour. CR-37 hardens it instead. |
| **Configurable timezone for cron and Diurnal** that defaults to anything other than host-local | Changing the default would shift every existing schedule on hosts without `TZ`. CR-26 fixes only the trip query and documents `TZ`. |

---

## Refuted claims from `.planning/codebase/CONCERNS.md`

| # | Claim (CONCERNS.md line) | Verdict | Evidence / real issue |
|---|---|---|---|
| R1 | "Hacky binding … binds the first TripTimerApp … only the last TripTimerApp across all devices gets wired" (:15-16) | **False** | `buttonApp` is a per-device local and `tripTimerApp` is a fresh pattern variable each iteration (`Conductor.cs:75,96`), so every TripTimerApp is bound to its own device's ButtonApp. The real defect nearby is CR-06 (duplicate Init fires the handler twice). |
| R2 | "`HttpPublisher` uses `new HttpClient()` … sockets not pooled … socket exhaustion" (:27-30) | **Overstated** | `new HttpClient()` is true (`HttpPublisher.cs:12`), but the publisher is a singleton, so one long-lived client pools connections and there is no socket exhaustion. The real problems are CR-02: no exception handling, 100 s timeout, undisposed responses. |
| R3 | "`GetNextDepartures` uses only `DepartureTimeEstimated`, ignoring real-time data" (:67-68) | **False and self-contradictory** | In TfNSW, `departureTimeEstimated` *is* the real-time value, falling back to planned (NSwag docs around `TripPlannerClient.nswag.cs:3359-3367`; fixture journey 1: estimated 05:37:54Z vs planned 05:43Z, `MONITORED`). The real issues are CR-10 (null estimate throws), CR-25 (snapshot), CR-28 (cancellations) and CR-37 (cache bypasses live data). |
| R4 | "`Newtonsoft.Json.Linq` imported but unused … unnecessary dependency on Newtonsoft at runtime" (:39-42) | **Partly false** | The usings are unused (`SlackConnector.cs:2`, `TripPlannerService.cs:4`), but Newtonsoft is not a direct package reference; it arrives transitively via SlackNet. Removing the usings removes no dependency, so this is cosmetic (CR-41, CR-43). |
| R5 | "`AppConfig.ConvertValue` uses invariant `Parse` … bad value causes unhandled `FormatException` at startup" (:51-54) | **Wrong on both points** | The parses use the *current* culture (`AppConfig.cs:131-155`). Values are parsed lazily: only `CronSchedule` fails at startup, while `ActiveTime`/`TimeToOrigin` fail at wake-up inside a background task. There the error is swallowed and the app never reschedules (CR-23, CR-08). |
| R6 | "Transient registration in `Program.cs` is effectively unused for apps" (:24) | **Partly false** | True for apps, but `DiagnosticsController` still resolves `AwtrixService` through DI (`DiagnosticsController.cs:27`), so the registration is not dead. |
| R7 | "`MqttClockRenderApp` has a potential race on `_mqttValue` / `_currentTime`" (:139-141) | **Not a real defect** | These are single reference and struct field writes; at worst the value is one tick stale, with no visible effect. CONCERNS.md missed the real bug in this class: the unbounded `SecondChanged` handler leak (CR-09). |
| R8 | "Trip planner caches by hour … stale cache can return past departures" (:154-156) | **Partly true, overstated** | The key is `trip_{o}_{d}_{HH}.json` (`TripPlannerService.cs:128`), but past entries are filtered by `PrepareForDepartTime > Clock.Now` in `BuildMessage`, and the cache is opt-in. The real issues are listed in CR-37: live data bypass, midnight rollover, crash on a bad file, no supported writer. |
| R9 | "Deserialisation can throw … ferry routes with `SUPPLEMENT`"; fix: "add a `JsonException` handler in `GetNextDepartures`" (:163-167) | **Mostly false; wrong fix** | `LineType` is `string` (`TripPlannerClient.nswag.cs:5139`), so `SUPPLEMENT` cannot throw. The "BUGGY" `footPathInfo` property is commented out (`:3115`). The generated client already catches `JsonException` and rethrows it as `TripPlannerException` (`:2052`, `:2068`), so a `JsonException` handler in the service would never fire for API responses. The residual risk is strict enums (CR-38); catch `TripPlannerException`/`HttpRequestException` (CR-10). |
| R10 | "DiurnalApp: a bad time entry causes a swallowed exception that produces no brightness changes" (:197-200) | **Partly wrong** | A bad time *key* is logged at Error and skipped, and other entries still apply. A bad *value* (`Brightness=abc` or `=300`) is **not** swallowed: it fails host start in the replay or crashes the process at trigger time. An unknown setting *name* crashes via empty `AwtrixSettings.ToString()` (CR-01, CR-21). |

**Confirmed but understated in CONCERNS.md:**
- The `.Result` in timer callbacks can crash the process (CR-01).
- The multi-device Init loop duplicates button, Diurnal and Slack handlers (CR-06).
- The `ExecuteNow` transient app is a permanent cron-armed leak (CR-07).
- The `ScheduledApp._cts` race produces duplicate waiters and orphaned CTS (CR-08).
- The ButtonApp `.Wait()` crashes startup when the broker is down (CR-05).
