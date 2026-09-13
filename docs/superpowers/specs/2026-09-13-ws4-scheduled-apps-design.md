# WS4 ScheduledApp Engine and Scheduled Apps — Design

- **Date:** 2026-09-13
- **Branch:** `feature/redo`
- **Source:** `code-review.md` → "WS4. ScheduledApp engine and scheduled apps"
- **Findings:** CR-08, CR-09, CR-18, CR-19, CR-31, CR-39, plus five minor issues WS1 deferred to WS4 (§6.7)
- **Depends on:**
  - WS1 (`docs/superpowers/specs/2026-09-13-ws1-runtime-resilience-design.md`, complete): tick contract, `FireAndLog`, `IClock` backed by `TimeProvider`, publishers never throw. The WS1 fix rounds already gave `ScheduledApp` per-activation CTS ownership behind `_ctsLock`/`_disposed`, `RunContinuationsAsynchronously` waits, and TripTimer unsubscribing before `AppClear`. WS4 replaces that interim engine and keeps its guarantees.
  - WS2 (`docs/superpowers/plans/2026-09-13-ws2-mqtt-connector.md`): `IMqttConnector.Subscribe` never throws; `MqttRenderApp` attaches its handler before subscribing (CR-33).
  - WS3 (`docs/superpowers/specs/2026-09-13-ws3-conductor-lifecycle-design.md`): `IAwtrixApp.InitAsync` (at most once), `IAwtrixApp : IDisposable, IAsyncDisposable`, the §5 lifecycle contract, the registry, and `ExecuteNow` → `AppExecutionResult`.
- **Written against:** the code shape after WS3. Plan Task 0 checks it.
- **Plan:** `docs/superpowers/plans/2026-09-13-ws4-scheduled-apps.md`
- **Status:** Approved for implementation. The owner was unavailable, so the planner made the decisions below and recorded them for review.

---

## 1. Goals

1. **A single, race-free activation engine in `ScheduledApp` (CR-08).**
   - At most one pending cron wait and at most one current activation, whatever mix of cron wake-ups, `ExecuteNow` calls and disposal happens.
   - Every transition runs under one lock.
   - An app-lifetime CTS ends everything at disposal.
   - Nothing after disposal re-arms or re-activates.
2. **`ActiveTime` applies to cron runs and manual runs alike (CR-18).**
3. **Scheduling never silently stops.**
   - A failure while waiting is logged and retried.
   - Delays longer than 49.7 days are chunked.
4. **Subclasses get explicit activate/deactivate hooks** and never see a `CancellationTokenSource`. Deactivation always runs once per activation that got as far as activating, including when activation threw.
5. **`MqttClockRenderApp` stops publishing when its window ends (CR-09).**
6. **TripTimer ends cleanly when no departures are left (CR-19).** It publishes no `{}` payload, and it ends through the activation it belongs to.
7. **One dispose pattern across all apps (CR-31):**
   - idempotent
   - async path awaited by Conductor
   - sync path never blocks
   - apps unsubscribe from events
   - no `Dismiss` of other apps' notifications
8. **Tests (CR-39):**
   - lifecycle tests on `FakeTimeProvider`
   - pinned test clocks in the scheduled-app tests
   - `ValueMapsTests.cs` restored
9. **Seams for WS6:** a periodic departure refresh can use the activation's token, replace departures atomically and end the activation, with no engine changes.

## 2. Non-goals

| Not in WS4 | Owner |
|---|---|
| Diurnal catch-up and validation, Slack null-safety, `DoubleClickDetector`, ValueMap setter table | WS5 (WS4 only adds one-method unsubscribe overrides to `DiurnalApp`/`SlackStatusApp`/`ButtonApp`; §4 D9) |
| Periodic departure refresh, retry, cancellable `ITripPlannerService` (CR-25, CR-29) | WS6 (§7 hand-off) |
| Case-insensitive config/ValueMap keys (CR-22); eager `ActiveTime`/`CronSchedule` validation (CR-23) | WS7 |
| Configurable timezone for cron (cron stays host-local, see Deferred) | Owner decision |
| `IMqttConnector.Unsubscribe` when an MqttRender window ends | Not planned: the broker subscription is harmless and re-subscribing re-delivers the retained value |
| Rate-limiting "device offline" warnings; a single-flight guard for per-second publishes | Deferred (WS1 §8) |
| Conductor changes | None needed. WS3's registry, `ExecuteNow` and bounded `DisposeAsync` are used as they are. |

## 3. Constraints

- **Config backward compatibility is mandatory.**
  - `CronSchedule` keeps NCrontab 5-field format, evaluated in host-local time (`TimeProvider.LocalTimeZone`; `TZ` in Docker).
  - `ActiveTime` keeps `TimeSpan.Parse` format (for example `"01:00:00"`).
  - No keys or env vars are added, renamed or reinterpreted.
- **No new configuration.** New code constants:
  - `ScheduledApp.MaxDelayChunk` = 1 day
  - `ScheduledApp.WaitRetryDelay` = 1 minute
  - `ScheduledApp.DeactivationTimeout` = 5 s
- **MQTT topics, HTTP URLs, API routes and response codes do not change.**
- **Target `net10.0`; no package changes.** `Microsoft.Extensions.TimeProvider.Testing` 10.0.0 is already in `test/Test`.
- **WS5 compatibility:** do not change:
  - the `AwtrixApp` constructor signature
  - `protected abstract void Initialize()`
  - `FireAndLog`
  - the `DiurnalApp`/`SlackStatusApp` constructors

  `ScheduledApp.cs` must not reference WS5 types.
- **Tests are deterministic:**
  - `FakeTimeProvider`, `TaskCompletionSource` gates and channels.
  - No `Task.Delay`/`Thread.Sleep`.
  - `WaitAsync(5 s)` is used only as a failure guard.
  - One retry test uses `SpinWait.SpinUntil` (bounded) to observe a thread-pool continuation.
- **No step runs the app**, a broker or a clock.

## 4. Design decisions

### D1. Activation object and hooks replace `ActivateScheduledWork(CancellationTokenSource)`

- **New public types in `src/api/Apps/ScheduledActivation.cs`:**
  - `public enum ActivationTrigger { Cron, Manual }`
  - `public sealed class ScheduledActivation` with:
    - `int Number` (1-based per app)
    - `ActivationTrigger Trigger`
    - `DateTimeOffset StartedAt`
    - `TimeSpan ActiveTime`
    - `CancellationToken Token` (captured at construction, so reading it never throws `ObjectDisposedException`)
    - `bool IsEnded`
    - `void Complete()` (idempotent, thread-safe, never throws, never runs deactivation inline)
- **Internal members:**
  - `WasActivated`
  - `Task Ended`, a `RunContinuationsAsynchronously` TCS completed by a token registration
  - `Release()`, which disposes the underlying CTS
- **The token is cancelled by any of:**
  - `Complete()`
  - `ActiveTime` elapsing, via a timeout CTS on the clock's `TimeProvider`
  - being superseded by a newer activation
  - app disposal, via the linked app-lifetime token
- **`internal sealed class CancellationScope`** (`src/api/Apps/CancellationScope.cs`) wraps "linked CTS + optional `TimeProvider` timeout":
  - `Cancel()` and `Dispose()` share a lock and a disposed flag, so a late `Cancel()` is a no-op, not an `ObjectDisposedException`.
  - Timeouts are clamped to [0, `uint.MaxValue - 1` ms]. That upper bound is the `CancellationTokenSource` timer limit.
- **Hooks on `ScheduledApp<TConfig>`:**
  - `protected abstract Task OnActivateAsync(ScheduledActivation activation)` wires up the window (subscribe, fetch) and **returns once wired**. The base awaits the end of the activation.
  - `protected virtual Task OnDeactivateAsync(ScheduledActivation activation)` unsubscribes. The default does nothing. The base calls `AppClear()` afterwards, so hooks do not clear.
  - `protected ScheduledActivation? CurrentActivation` is the activation whose handlers are wired, or null. Event handlers use it as a guard: `if (CurrentActivation is not { IsEnded: false }) return;`.
- **Removed:**
  - `ActivateScheduledWork`
  - `WaitForCancellation`
  - `protected CancellationTokenSource _cts`
  - `IsScheduled`
- **Rejected:** keeping the "subclass awaits cancellation itself" shape with a better token. Every subclass would have to repeat try/finally teardown, and the WS1 bugs (b) and (c) came from exactly that duplication.

### D2. State machine

The state lives in private fields, all mutated under `_gate`:
- `_pendingWait : CancellationScope?`
- `_active : ScheduledActivation?`
- `_lastRun : Task`, which completes when the latest activation's teardown and re-arm finish
- `_nextWakeUp : DateTimeOffset?`
- `_activationCount`
- `_disposed`
- `_lifetime`, a CTS that is never replaced and never disposed. It has no timer, and not disposing it removes the race between a late linked-CTS creation and disposal.

Transitions:

| Trigger | Guard (under `_gate`) | Effect |
|---|---|---|
| `Initialize()` | — | parse cron, `ArmNextWait()` |
| `ArmNextWait()` | not disposed, cron parsed, no active activation, no pending wait | create a wait scope linked to `_lifetime`; its synchronous prefix computes `due`, sets `_nextWakeUp` and registers the first delay timer before returning |
| wait elapses | wait is still `_pendingWait` | `StartActivation(Cron)` |
| `ExecuteNow()` | not disposed | `StartActivation(Manual)` |
| `StartActivation` | not disposed; for cron, not superseded | read `ActiveTime`; take `_pendingWait` (cancel it) and `_active` (complete it); create the new activation; replace `_lastRun` with a new TCS; then, outside the lock, run it |
| activation ends | — | `OnDeactivateAsync` (bounded), `AppClear`, clear `_active` if still current, release, `ArmNextWait()` if still current and not disposed, then complete its `_lastRun` TCS |
| dispose | — | set `_disposed`, cancel `_lifetime` (ends the wait and the activation) |

- **`ExecuteNow` before `InitAsync`** (existing tests and Conductor init-failure paths) works. With no parsed cron, nothing is re-armed afterwards.
- **Test seams:** `internal DateTimeOffset? NextWakeUp` and `internal Task LastRun`.

### D3. Activations are serialised: teardown finishes before the next activation wires up

- The run captures `previousRun = _lastRun` at start and awaits it before `AppClear` + `OnActivateAsync`. The previous run's task never faults, and its teardown is bounded by `DeactivationTimeout` plus the publisher timeout.
- **Why:** it fixes WS1 deferred (b) structurally. Handlers are method-group delegates, so an old `-=` running after a new `+=` removed the new one. With serialisation that interleaving is impossible. It also orders the old window's final `AppClear` before the new window's first publish.
- **Cost:** a superseding `ExecuteNow` takes effect on the display after the old teardown completes, typically milliseconds and at most about 10 s with an offline HTTP device. `ExecuteNow` still returns immediately.
- **Synchronous prefix preserved:** when nothing precedes the run and the dependencies return completed tasks, `ExecuteNow` runs through `OnActivateAsync` synchronously on the caller's thread. WS2's retained-message tests and WS3's double-click test rely on this.

### D4. `ActiveTime` for both triggers (CR-18)

- It is read once per activation in `StartActivation`, inside try/catch.
- **Missing or unparsable** (today `GetConfig<TimeSpan>` unboxes null and throws) → logged at Error, and the activation ends immediately. Scheduling continues, so the next occurrence still fires, and the next `ExecuteNow` logs again.
- **Zero or negative** → logged at Warning; the activation ends immediately.
  - This is the effective behaviour today for cron runs (`CancelAfter(0)`).
  - Manual runs previously ran forever. That is the CR-18 bug, so the change is intended.
- **Longer than 49.7 days** → clamped.
- An activation that ended before wiring (superseded while waiting, zero `ActiveTime`, disposed) never calls `OnActivateAsync`/`OnDeactivateAsync` and does not publish.

### D5. Cron occurrences during an active run are skipped

- No wait is armed while an activation is current. When it ends, the next occurrence is computed from "now".
- **Example:** a manual run at 07:50 with a 12 h window absorbs the 08:00 occurrence.
- **Rejected:** keeping a wait armed during a run and letting the cron occurrence supersede the run. That restarts the display mid-window, and it reintroduces two concurrent timers per app, which is what CR-08 scenario A is about.

### D6. Time source, cron evaluation, chunked delays, retry

- **`IClock` gains a default interface member** `TimeProvider TimeProvider => TimeProvider.System;`. `Clock` returns its injected provider.
  - Why: production has one time source (DI already builds `Clock(TimeProvider.System)`), tests pass `new Clock(fakeTimeProvider)`, and neither `Conductor` nor the app constructors change.
  - `MockClock` needs no edit; it gets the system provider.
- **Scheduling and activation times** use `Clock.TimeProvider`:
  - `GetLocalNow()` for cron evaluation and `StartedAt`
  - `GetUtcNow()` for remaining-delay maths
  - `Task.Delay(TimeSpan, TimeProvider, CancellationToken)`
  - `CancellationTokenSource(TimeSpan, TimeProvider)`
  - Subclasses keep using `Clock.Now` for display logic.
- **Cron:**
  - `next = CrontabSchedule.GetNextOccurrence(now.LocalDateTime-equivalent)`
  - `due = new DateTimeOffset(next, LocalTimeZone.GetUtcOffset(next))`
  - This fixes a latent bug: today `DateTime - DateTimeOffset` used the machine zone, not the clock's.
- **Chunked delay:** loop `remaining = due - GetUtcNow()`, `Task.Delay(min(remaining, 1 day))` until `remaining <= 0`.
  - A clock change during a long wait is picked up at the next chunk boundary.
  - Awaits use `ConfigureAwait(ConfigureAwaitOptions.ForceYielding)`, so cancellation continuations never run inline on a canceller's thread (a tick handler or `Dispose`).
- **Wait failure** (any non-cancellation exception while computing or waiting):
  - logged at Error with the exception
  - retried after `WaitRetryDelay` (1 min) on the same wait scope
  - no hot loop, and the app never silently stops

### D7. Deactivation is bounded and always runs

- The run's `finally` does four things:
  1. `activation.Complete()`. This ends the window if `OnActivateAsync` threw.
  2. If the activation was wired: `OnDeactivateAsync(...).WaitAsync(DeactivationTimeout, TimeProvider)`, with timeout → Warning and exception → Error.
  3. `CurrentActivation = null`.
  4. `AppClear()`.
- **Exceptions from `OnActivateAsync`:**
  - `OperationCanceledException` while the activation is ended → Debug. This covers WS1 deferred (c): a superseded activation reading its token gets an OCE, never `ObjectDisposedException`, and nothing is logged at Error.
  - Anything else → Error.

### D8. One dispose pattern (CR-31)

`AwtrixApp<TConfig>` owns it:

```csharp
public async ValueTask DisposeAsync()   // once; Conductor's path
{ if (!TryBeginDispose()) return; ReleaseResources(); await DisposeCoreAsync(); GC.SuppressFinalize(this); }

public void Dispose()                   // once; never blocks
{ if (!TryBeginDispose()) return; ReleaseResources(); _ = FireAndLog(DisposeCoreAsync, nameof(Dispose)); GC.SuppressFinalize(this); }

protected virtual void ReleaseResources() { }          // sync, non-blocking: cancel work, unsubscribe
protected virtual Task DisposeCoreAsync() => AppClear(); // final publishes; overrides call base last
```

- **Neither method is virtual.** The WS3 `virtual DisposeAsync` and its `ScheduledApp` override are removed. Subclasses use the two hooks.
- **`ScheduledApp`:**
  - `ReleaseResources` sets `_disposed` and cancels `_lifetime`.
  - `DisposeCoreAsync` awaits `LastRun` (bounded by `DeactivationTimeout`), then `base.DisposeCoreAsync()`.
  - The run's teardown and the final clear stay inside WS3's 10 s `AppDisposeTimeout` in the normal case.
- **`Dismiss` is no longer called on dispose.** It removes notifications posted by other apps (CR-31). Notifications are transient on Awtrix. WS3 tests that asserted `Dismiss` are updated.
- **`TripTimerApp`'s `new Dispose(bool)`/`Dispose()` are deleted:**
  - (d): they were never reached through `IAwtrixApp`.
  - (e): they blocked on `AppClear`.
  - Its teardown now runs through `OnDeactivateAsync`.
- **Unsubscribe overrides:**
  - `ButtonApp`: `MessageReceived`
  - `DiurnalApp`: `MinuteChanged`
  - `SlackStatusApp`: `UserStatusChanged`

  Each is a one-method `ReleaseResources` override. WS5's plan does not unsubscribe, so there is no duplication. See the Risks for merge handling.

### D9. `MqttRenderApp` / `MqttClockRenderApp` (CR-09)

- **`MqttRenderApp`:**
  - `OnActivateAsync` attaches `MessageReceived` (before subscribing, CR-33), then `await Subscribe(ReadTopic).WaitAsync(activation.Token)`. A SUBACK that never arrives cannot keep a window open past `ActiveTime`.
  - `OnDeactivateAsync` detaches.
  - `RawMessageReceived` ignores messages when `CurrentActivation` is null or ended. This covers a dispatch already in flight during teardown.
- **`MqttClockRenderApp`:**
  - `OnActivateAsync` attaches `SecondChanged`, then calls `base`.
  - `OnDeactivateAsync` detaches, then calls `base`.
  - `ClockTick` has the same guard.

### D10. `TripTimerApp` (CR-19, WS1 deferred a)

- **`BuildMessage`** becomes `internal AwtrixAppMessage? BuildMessage(DateTime tickTime)`. It is pure: no cancellation, and it returns `null` when no alarm is in the future.
- **`ClockTickSecond`:**
  1. Capture `CurrentActivation`; return if it is null or ended (deferred a).
  2. In `FireAndLog`, build the message.
  3. If the message is null: log Information, `activation.Complete()`, return. Nothing is published.
  4. Otherwise `AppUpdate`, unless the activation ended meanwhile.
- **`OnActivateAsync`:**
  1. `Notify("Starting trip timer")`
  2. `GetNextDepartures(...).WaitAsync(activation.Token)`
  3. `SetDepartures(...)`
  4. attach `SecondChanged`
- **`OnDeactivateAsync`** detaches `SecondChanged`.
- **The `MinuteChanged` debug-log handler is removed.** It only logged, and it was one more subscription to leak.
- **`internal IReadOnlyList<TripSummary> NextDepartures`** is backed by a volatile reference. `internal void SetDepartures(IEnumerable<TripSummary>)` rounds each departure to the minute, swaps the reference and logs the alarm stages. Replacing the list is atomic against ticks on the timer thread (WS6).

### D11. Tests

- **`ScheduledAppTests` is rewritten** around a `TestScheduledApp` that records `activate#N`/`deactivate#N` and publishes activations to channels.
  - Time is `new Clock(new FakeTimeProvider(2026-09-13T07:59Z))`; the fake's local zone is UTC.
  - Cron timers are fired with `Advance`. Tests wait for the "activated" signal before advancing past `ActiveTime`, because the timeout timer is created on the thread-pool continuation.
  - One test uses a tiny `TimeProvider` wrapper whose `LocalTimeZone` throws once, to prove retry.
- **MqttRender and TripTimer tests** move from `DateTimeOffset.Now` to `FakeTimeProvider`/`MockClock` pinned to fixed instants.
- **`test/Test/Configs/ValueMapsTests.cs` is restored** (CR-39):
  - The `ValueMapJsonConverter` dependency (removed type) is dropped.
  - Deserialisation uses PascalCase keys, as `appsettings.json` does; case-insensitive keys are WS7 CR-22.
  - The ad-hoc "Debug*" tests are dropped.
  - Tests that duplicate `ValueMapDecorateTests` are not restored.
  - Added: first-match-wins, and the two shipped MqttRender matchers (`^-`, `^(?!-).*`).
  - The empty `ValueMatcher` stays non-matching, which WS5 D8 also preserves.

## 5. Behaviour changes visible to users

| Change | Why |
|---|---|
| `POST api/app/{TripTimer,MqttRender}/start` runs now end after `ActiveTime` (they used to run until restart) | CR-18 |
| A cron occurrence inside an already-active window is skipped, not restarted | D5 |
| Shutdown no longer sends `notify/dismiss` | CR-31 |
| After the last departure, TripTimer clears its slot instead of leaving a blank `{}` page | CR-19 |
| A missing `ActiveTime` logs an Error per activation, and the app keeps scheduling (it used to stop scheduling silently) | D4, CR-08 |

## 6. Acceptance criteria

### 6.1 CR-08: engine re-entrancy
1. After `InitAsync`, exactly one wait is armed, for the next cron occurrence. Advancing to it activates once with `Trigger == Cron`. Advancing `ActiveTime` deactivates once and re-arms for the following occurrence. *(ScheduledAppTests)*
2. `ExecuteNow` during an active run (the old teardown is gated):
   - the old activation is ended
   - the new activation does not wire up until the old teardown completes
   - the event order is `activate#1, deactivate#1, activate#2`
   - after the new run ends, a single wait is armed
   *(ScheduledAppTests)*
3. Two `ExecuteNow` calls while the first activation's in-flight work is still pending:
   - the second activation runs and is not ended
   - the first reading its token raises OCE
   - nothing is logged at Error
   *(ScheduledAppTests)*
4. `DisposeAsync` during a run:
   - it deactivates once
   - `NextWakeUp` is null
   - a later `ExecuteNow` or 2 days of advanced time cause no activation
   - no Error is logged
   *(ScheduledAppTests)*
5. `DisposeAsync` completes after `DeactivationTimeout` when `OnDeactivateAsync` hangs. *(ScheduledAppTests)*
6. A yearly cron (`0 0 1 1 *`, due in over 49.7 days) activates when time reaches it. `NextDelayChunk` never exceeds 1 day. *(ScheduledAppTests)*
7. A failure while computing the next occurrence is logged once at Error, and the wait re-arms after `WaitRetryDelay`. *(ScheduledAppTests)*
8. No `CancellationTokenSource` field or parameter is visible to subclasses (`grep -n "CancellationTokenSource" src/api/Apps/ScheduledApp.cs src/api/Apps/TripTimer src/api/Apps/MqttRender` → no matches). There are no `ActivateScheduledWork` or `WaitForCancellation` symbols (code review).

### 6.2 CR-18: manual runs have no ActiveTime limit
1. `ExecuteNow` while waiting:
   - it activates with `Trigger == Manual` and cancels the wait (passing the cron instant does not activate)
   - it is still active at `ActiveTime - 1 s`
   - it deactivates at `ActiveTime`
   - it re-arms for the next occurrence after the run
   *(ScheduledAppTests)*
2. With no `ActiveTime` configured, `ExecuteNow` logs one Error, never calls `OnActivateAsync`, and leaves the cron wait armed. *(ScheduledAppTests)*

### 6.3 CR-09: MqttClockRenderApp leaks `SecondChanged`
1. After the window ends:
   - `SecondChanged` has been removed once
   - a raised tick publishes nothing
   *(MqttClockRenderAppTests)*
2. After three complete activations and a fourth `ExecuteNow`, one tick produces exactly one `AppUpdate`. *(MqttClockRenderAppTests)*
3. A tick delivered to the handler after deactivation publishes nothing. *(MqttClockRenderAppTests)*
4. MqttRender:
   - a message after the window is not rendered and the handler was removed once
   - an in-flight message dispatch after deactivation is ignored
   - a `Subscribe` that never completes does not keep the run alive past `ActiveTime`
   *(MqttRenderAppTests)*

### 6.4 CR-19: TripTimer publishes `{}` and cancels a shared field
1. When the only alarm has passed, a tick:
   - publishes no `AppUpdate`
   - ends the activation
   - unsubscribes `SecondChanged` once
   - clears the slot (`AppClear` ×2: activation start + end)
   *(TripTimerAppTickTests)*
2. That tick returns promptly while the final `AppClear` is slow. *(TripTimerAppTickTests)*
3. `BuildMessage` returns `null` when there are no future departures. The other boundary tests are unchanged. *(TripTimerAppBoundaryTests)*
4. A tick while active publishes the countdown to the next alarm. *(TripTimerAppTickTests)*

### 6.5 CR-31: dispose pattern
1. `DisposeAsync` twice, then `Dispose`, runs `ReleaseResources` once and `AppClear` once. *(AwtrixAppTests)*
2. `Dispose` returns while `AppClear` never completes. *(AwtrixAppTests)*
3. `TripTimerApp.Dispose()` while active returns while `AppClear` never completes (deferred e). *(AppDisposalTests)*
4. `DiurnalApp`, `SlackStatusApp` and `ButtonApp` remove their event handlers on `DisposeAsync`. After dispose, a button message raises no `Click`. *(AppDisposalTests)*
5. No app calls `Dismiss` on dispose. *(ScheduledAppTests, ConductorStartupTests)*
6. Structure (code review):
   - `grep -n "new protected\|public void Dispose" src/api/Apps/TripTimer/TripTimerApp.cs src/api/Apps/ScheduledApp.cs` → no matches (deferred d)
   - `grep -rn "\.Wait()" src/api/Apps` → no matches (`SlackStatusApp`'s `.Result` is WS5's)

### 6.6 CR-39: test gaps
1. `test/Test/Configs/ValueMapsTests.cs` compiles and passes, with no commented-out code. *(ValueMapsTests)*
2. `grep -n "DateTimeOffset.Now" test/Test/Apps/ScheduledAppTests.cs test/Test/Apps/TripTimerAppTests.cs test/Test/Apps/MqttRender/*.cs test/Test/Apps/TripTimer/*.cs` → no matches.
3. The lifecycle tests in 6.1-6.4 exist and use `FakeTimeProvider`. They contain no `Task.Delay`.

### 6.7 WS1 deferred minors
| # | Issue | Criterion |
|---|---|---|
| a | A late TripTimer tick after Dispose hits a null `_cts` and logs Error | A tick invoked after `DisposeAsync` publishes nothing and logs no Error *(TripTimerAppTickTests)* |
| b | A late teardown of an old activation removes the new activation's handler | 6.1 §2 (engine order) and a TripTimer test: after a superseding `ExecuteNow` with a slow old `AppClear`, `SecondChanged` is added twice and removed once, and one tick publishes once *(ScheduledAppTests, TripTimerAppTickTests)* |
| c | A superseded activation reading `cts.Token` throws `ObjectDisposedException` logged at Error | 6.1 §3 |
| d | `TripTimerApp.Dispose(bool)` hides the base method and never runs | 6.5 §6 grep; teardown lives in `OnDeactivateAsync` |
| e | `TripTimerApp.Dispose` blocks on `AppClear` | 6.5 §3 |

## 7. Seams handed to WS6 (trip planner refresh, CR-25)

- **Where:** start a refresh loop at the end of `TripTimerApp.OnActivateAsync`: `_ = FireAndLog(() => RefreshLoopAsync(activation), "Refresh departures")`.
  - Loop on `Task.Delay(interval, Clock.TimeProvider, activation.Token)`.
  - Call `SetDepartures(...)` on success and keep the last good list on failure.
  - The loop ends by itself when the activation ends (OCE → Debug in `FireAndLog`). No deactivation code is needed.
- **Cancellation:** pass `activation.Token` to a cancellable `ITripPlannerService.GetNextDepartures` (CR-29). Until then, `.WaitAsync(activation.Token)` is used.
- **Completion:** to re-query before giving up (CR-19/CR-25), replace `activation.Complete()` in `ClockTickSecond` with a single-flight "refresh, then complete if still empty" call. `CurrentActivation`/`IsEnded` guards already make late results harmless.
- **Tests:** `FakeTimeProvider` + `new Clock(fake)`, and `TripTimerApp.LastRun` for awaiting teardown.

## 8. Risks

- **WS5 merge.** WS5 Tasks 3-4 rewrite `DiurnalApp`/`SlackStatusApp`, and its plan was written before WS4.
  - If WS5 replaces a whole file, it must keep the `ReleaseResources` override added here.
  - WS4 commit messages call this out, and `AppDisposalTests` fails loudly if it is lost.
- **Subclass signature change.** `ActivateScheduledWork` → `OnActivateAsync`/`OnDeactivateAsync` breaks any out-of-tree subclass. There are none in the repo.
- **FakeTimeProvider continuation ordering.**
  - Timers fire inside `Advance`, and continuations run on the pool.
  - Tests wait for a signal before the next `Advance` whenever the next timer is created by a continuation.
  - The only multi-timer case (chunked yearly wait) pumps `Advance(TimeSpan.Zero)` until activation, bounded by the 5 s guard.
- **In-flight publish vs final clear.** A per-second `AppUpdate` that started just before deactivation can land after the final `AppClear`, which leaves one stale frame until the next activation. Guards shrink the window to the publish duration. A single-flight publish guard is deferred.
- **Dispose budget.** Worst case with an offline HTTP device: 5 s `DeactivationTimeout` + 5 s `AppClear` = WS3's 10 s `AppDisposeTimeout`. Conductor may abandon the last clear. This is the same visible effect as WS3 §8 (Docker 10 s grace).
- **Default interface member on `IClock`.** A future `Mock<IClock>` would return null for `TimeProvider` unless configured. There are no mocks today; test code uses `MockClock` or `Clock`.
- **WS1-WS3 drift.** Plan Task 0 checks the assumed shapes and gives adaptation rules.
