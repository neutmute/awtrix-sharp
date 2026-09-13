# WS4 ScheduledApp Engine and Scheduled Apps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:**
- Scheduled apps run on one race-free activation engine: one pending wait, one activation, ActiveTime for cron and manual runs, chunked delays and retry, serialised teardown.
- MqttClockRender and TripTimer tear down cleanly.
- Every app shares one idempotent, non-blocking dispose pattern.
- The ValueMap tests are restored.

**Architecture:**
- **`ScheduledApp`** owns a lock-guarded state machine over an app-lifetime CTS. Each activation is a `ScheduledActivation` (token, `Complete()`, ActiveTime timeout on the clock's `TimeProvider`).
- **Hooks:** subclasses implement `OnActivateAsync`/`OnDeactivateAsync` and guard event handlers with `CurrentActivation`.
- **Dispose:** `AwtrixApp` implements `DisposeAsync`/`Dispose` once via `ReleaseResources` (sync) + `DisposeCoreAsync` (final clears).

**Tech Stack:** .NET 10, ASP.NET Core, xUnit 2.9, Moq 4.20, NCrontab 3.3.3, Microsoft.Extensions.TimeProvider.Testing 10.0.0

**Spec:** `docs/superpowers/specs/2026-09-13-ws4-scheduled-apps-design.md`

## Global Constraints

- **Prerequisites:** WS1, WS2 and WS3 are fully committed, and `git status --short` is clean before starting. Another agent may be committing to this tree; never start while it is dirty.
- **Config compatibility:**
  - `CronSchedule` (NCrontab, host-local) and `ActiveTime` (`TimeSpan.Parse`) formats are unchanged.
  - No `appsettings.json` keys or `AWTRIXSHARP_*` / `TRANSPORTOPENDATA__APIKEY` env vars are added, renamed or reinterpreted.
- **No new configuration.** Code constants:
  - `ScheduledApp<T>.MaxDelayChunk = TimeSpan.FromDays(1)`
  - `ScheduledApp<T>.WaitRetryDelay = TimeSpan.FromMinutes(1)`
  - `ScheduledApp<T>.DeactivationTimeout = TimeSpan.FromSeconds(5)`
- **No package changes.** Target `net10.0`.
- **Do not edit:**
  - `src/api/HostedServices/Conductor.cs`
  - `src/api/Program.cs`
  - `src/api/HostedServices/MqttConnector.cs`
  - `src/api/Interfaces/IAwtrixApp.cs`
  - `src/api/Apps/Configs/ValueMap.cs`
  - `src/api/Domain/AwtrixAppMessage.cs`
- **WS5 compatibility:** keep the `AwtrixApp` constructor signature, `protected abstract void Initialize()`, `FireAndLog`, and the `DiurnalApp`/`SlackStatusApp` constructors unchanged. `ScheduledApp.cs` must not reference Diurnal/Slack/Button/ValueMap types.
- **MQTT topics, HTTP URLs, API routes and status codes are unchanged.**
- **Tests:**
  - No `Task.Delay`/`Thread.Sleep`.
  - Time comes from `FakeTimeProvider` or `MockClock` pinned to fixed instants.
  - `WaitAsync(TimeSpan.FromSeconds(5))` is used only as a failure guard.
  - `SpinWait.SpinUntil(..., 5 s)` is allowed only where a thread-pool continuation must be observed.
- **Never run the app**, a broker or a clock. Unit tests only.
- **TDD per task:**
  1. Write the failing test.
  2. Run it filtered; it fails (a compile error counts).
  3. Implement.
  4. Run it filtered; it passes.
  5. Run the full `dotnet test` with 0 failed.
- **Commits:** explicit `git add <paths>`, never `-A` or `.`. Every commit message ends with exactly:
  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt
  ```
- **Shell:** run every command from the repo root `C:\CodeMine\awtrix-sharp` in Git Bash.

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/api/Apps/AwtrixApp.cs` | one dispose pattern (`ReleaseResources`, `DisposeCoreAsync`) | 1 |
| `src/api/Apps/Buttons/ButtonApp.cs`, `src/api/Apps/Diurnal/DiurnalApp.cs`, `src/api/Apps/SlackStatus/SlackStatusApp.cs` | unsubscribe in `ReleaseResources` | 1 |
| `src/api/Apps/ScheduledApp.cs` | interim dispose hooks (T1); new engine (T2) | 1, 2 |
| `src/api/Apps/CancellationScope.cs` (new) | safe linked CTS with a `TimeProvider` timeout | 2 |
| `src/api/Apps/ScheduledActivation.cs` (new) | `ScheduledActivation`, `ActivationTrigger` | 2 |
| `src/api/Interfaces/IClock.cs`, `src/api/Domain/Clock.cs` | `TimeProvider` on the clock | 2 |
| `src/api/Apps/TripTimer/TripTimerApp.cs` | drop hiding Dispose (T1); hooks (T2); CR-19 and guards (T4) | 1, 2, 4 |
| `src/api/Apps/MqttRender/MqttRenderApp.cs`, `MqttClockRenderApp.cs` | hooks (T2); CR-09 and guards (T3) | 2, 3 |
| `test/Test/Apps/AwtrixAppTests.cs` | dispose-pattern tests | 1 |
| `test/Test/Apps/AppDisposalTests.cs` (new) | event-app unsubscribe; TripTimer non-blocking dispose | 1 |
| `test/Test/HostedServices/ConductorStartupTests.cs` | no `Dismiss` on dispose (one assertion) | 1 |
| `test/Test/Apps/ScheduledAppTests.cs` | Dismiss assertions (T1); full rewrite (T2) | 1, 2 |
| `test/Test/Apps/TripTimer/TripTimerAppTickTests.cs` | interim (T2); final lifecycle tests (T4) | 2, 4 |
| `test/Test/Apps/TripTimer/TripTimerAppBoundaryTests.cs` | drop `_cts` seeding (T2); `BuildMessage` null API (T4) | 2, 4 |
| `test/Test/Apps/TripTimerAppTests.cs` | pinned clock | 4 |
| `test/Test/Apps/MqttRender/MqttRenderAppTests.cs`, `MqttClockRenderAppTests.cs` | fake time; CR-09 tests | 3 |
| `test/Test/Configs/ValueMapsTests.cs` | restored | 5 |

---

### Task 0: Verify WS1-WS3 shapes as assumed

**Files:** none modified.

- [ ] **Step 1: Check the tree and history**

Run: `git status --short` and `git log --oneline -20`.

Expected:
- The tree is clean. If it is not, stop: another agent is still working.
- The history includes these commits:
  - WS1 `refactor(di): interface seams for Conductor and apps; validated composition root`
  - WS1 `fix(ws1): per-activation CTS ownership in ScheduledApp`
  - WS2 `fix(mqtt-render): attach message handler before subscribing`
  - WS3 `fix(apps): async InitAsync and non-blocking ButtonApp subscribe (CR-05)`
  - WS3 `fix(lifecycle): bounded async app disposal at shutdown; SlackConnector stops without a client (CR-16)`

- [ ] **Step 2: Check the assumed shapes**

Run each command. The expected result follows each arrow.
- `grep -n "interface IAwtrixApp" src/api/Interfaces/IAwtrixApp.cs` → `public interface IAwtrixApp : IDisposable, IAsyncDisposable`
- `grep -n "Task InitAsync\|void ExecuteNow" src/api/Interfaces/IAwtrixApp.cs` → both present.
- `grep -n "public async Task InitAsync\|protected abstract void Initialize\|protected Task FireAndLog\|public void Dispose\|ValueTask DisposeAsync" src/api/Apps/AwtrixApp.cs` → `InitAsync`, `Initialize`, `FireAndLog`, `public void Dispose()` (body `AppClear().Wait();`) and `public virtual async ValueTask DisposeAsync()`.
- `grep -n "_cts\b\|_ctsLock\|CancelAndDispose\|public void Dispose\|Dispose(bool\|DisposeAsync\|ActivateScheduledWork\|WaitForCancellation" src/api/Apps/ScheduledApp.cs` → the WS1 fields and methods, `public void Dispose()`, `protected void Dispose(bool disposing)`, and a `public override async ValueTask DisposeAsync()`.
- `grep -n "new protected void Dispose\|public void Dispose\|ActivateScheduledWork\|DeactivateAsync\|_cts.Cancel" src/api/Apps/TripTimer/TripTimerApp.cs` → all present.
- `grep -n "MessageReceived += RawMessageReceived" -A1 src/api/Apps/MqttRender/MqttRenderApp.cs` → the handler is attached on the line before `await _mqttConnector.Subscribe(Config.ReadTopic);`.
- `grep -n "_mqttConnector.MessageReceived += RawMessageReceived" src/api/Apps/Buttons/ButtonApp.cs` → one match.
- `grep -n "_timerService.MinuteChanged += ClockTickMinute" src/api/Apps/Diurnal/DiurnalApp.cs` → one match.
- `grep -n "_slackConnector.UserStatusChanged += UserStatusChanged" src/api/Apps/SlackStatus/SlackStatusApp.cs` → one match.
- `grep -rn ": IClock" src test --include=*.cs | grep -v /obj/` → only `Clock` and `MockClock`.
- `grep -n "Dismiss" test/Test/Apps/ScheduledAppTests.cs test/Test/HostedServices/ConductorStartupTests.cs` → `Dispose_CancelsPendingWorkAndClearsApp` (`Times.AtLeastOnce`), `DisposeAsync_EndsActiveRunAndAwaitsDismissAndClear` (`Times.Once`), and `StartAsync_ScheduledAppWithoutCronSchedule_IsSkippedAndDisposed` (`awtrix.Verify(a => a.Dismiss(device), Times.Once);`).
- `grep -n "TimeProvider.Testing" test/Test/Test.csproj` → one match.
- `grep -n "InternalsVisibleTo" src/api/awtrix-api.csproj` → `Test`.

- [ ] **Step 3: Adapt if the landed code differs**

- **Different names:** use the landed names everywhere this plan uses the assumed ones (for example the WS3 test names in Task 1 Step 1). Record the mapping in the Task 1 commit body (`Adapted: …`).
- **WS3 `DisposeAsync` not virtual, or absent in `ScheduledApp`:** Task 1 still replaces whatever dispose members exist with the pattern given there.
- **No WS1-WS3 behaviour is changed** except where a task says so.

- [ ] **Step 4: Baseline**

Run: `dotnet test`
Expected: 0 failed. Note the passed and skipped counts.

---

### Task 1: One dispose pattern; event apps unsubscribe; no Dismiss on dispose (CR-31, WS1 deferred d/e)

**Files:**
- Modify: `src/api/Apps/AwtrixApp.cs` (replace the `Dispose`/`DisposeAsync` members)
- Modify: `src/api/Apps/ScheduledApp.cs` (replace the dispose members with `ReleaseResources`)
- Modify: `src/api/Apps/TripTimer/TripTimerApp.cs` (delete both `Dispose` members)
- Modify: `src/api/Apps/Buttons/ButtonApp.cs`, `src/api/Apps/Diurnal/DiurnalApp.cs`, `src/api/Apps/SlackStatus/SlackStatusApp.cs` (add a `ReleaseResources` override)
- Modify: `test/Test/Apps/AwtrixAppTests.cs` (`TestAwtrixApp` and two tests)
- Create: `test/Test/Apps/AppDisposalTests.cs`
- Modify: `test/Test/Apps/ScheduledAppTests.cs` (two assertions)
- Modify: `test/Test/HostedServices/ConductorStartupTests.cs` (one assertion)

**Interfaces:**
- Consumes: `IAwtrixApp : IDisposable, IAsyncDisposable` (WS3); `AwtrixApp.FireAndLog(Func<Task>, string)` (WS1).
- Produces (later tasks rely on these exact names):
  - `public ValueTask AwtrixApp<TConfig>.DisposeAsync()` (non-virtual, runs once)
  - `public void AwtrixApp<TConfig>.Dispose()` (non-virtual, runs once, never blocks)
  - `protected virtual void AwtrixApp<TConfig>.ReleaseResources()`
  - `protected virtual Task AwtrixApp<TConfig>.DisposeCoreAsync()` (default `AppClear()`)
  - `protected bool AwtrixApp<TConfig>.IsDisposed`

- [ ] **Step 1: Write the failing tests**

In `test/Test/Apps/AwtrixAppTests.cs`, add inside `internal class TestAwtrixApp` (after `InitializeCallCount`):

```csharp
        public int ReleaseResourcesCallCount { get; private set; }

        protected override void ReleaseResources()
        {
            ReleaseResourcesCallCount++;
            base.ReleaseResources();
        }
```

Then add inside `public class AwtrixAppTests`, after `Dispose_CallsAppClear`:

```csharp
        [Fact]
        public async Task DisposeAsync_ThenDisposeAgain_ReleasesAndClearsOnce()
        {
            var sut = CreateSut("MyApp");

            await sut.DisposeAsync();
            await sut.DisposeAsync();
            sut.Dispose();

            Assert.Equal(1, sut.ReleaseResourcesCallCount);
            _mockAwtrixService.Verify(x => x.AppClear(_address, "MyApp"), Times.Once);
        }

        [Fact]
        public async Task Dispose_ReleasesSynchronously_AndDoesNotWaitForAppClear()
        {
            // CR-31: the sync path used to block on AppClear().Wait()
            var sut = CreateSut("MyApp");
            var neverCompletes = new TaskCompletionSource<bool>();
            _mockAwtrixService.Setup(x => x.AppClear(_address, "MyApp")).Returns(neverCompletes.Task);

            await Task.Run(() => sut.Dispose()).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, sut.ReleaseResourcesCallCount);
            _mockAwtrixService.Verify(x => x.AppClear(_address, "MyApp"), Times.Once);
        }
```

Create `test/Test/Apps/AppDisposalTests.cs`:

```csharp
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.Diurnal;
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Apps.SlackStatus;
using AwtrixSharpWeb.Apps.TripTimer;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MQTTnet;
using Test.Apps.MqttRender;

namespace Test.Apps
{
    /// <summary>
    /// CR-31: every app releases its event subscriptions on dispose, and the synchronous Dispose never blocks.
    /// </summary>
    public class AppDisposalTests
    {
        private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);
        private static readonly DateTimeOffset Noon = new(2026, 9, 13, 12, 0, 0, TimeSpan.FromHours(10));

        private static AwtrixAddress Address() => new() { BaseTopic = "awtrix/clock1" };

        [Fact]
        public async Task DiurnalApp_DisposeAsync_UnsubscribesMinuteChanged()
        {
            var timer = new Mock<ITimerService>();
            var app = new DiurnalApp(NullLogger.Instance, new MockClock(Noon), timer.Object,
                AppConfig.Empty().WithName(AppNames.DiurnalApp), Address(), new Mock<IAwtrixService>().Object);
            await app.InitAsync();

            await app.DisposeAsync();

            timer.VerifyAdd(t => t.MinuteChanged += It.IsAny<EventHandler<ClockTickEventArgs>>(), Times.Once);
            timer.VerifyRemove(t => t.MinuteChanged -= It.IsAny<EventHandler<ClockTickEventArgs>>(), Times.Once);
        }

        [Fact]
        public async Task SlackStatusApp_DisposeAsync_UnsubscribesUserStatusChanged()
        {
            var slack = new Mock<ISlackConnector>();
            var config = new SlackStatusAppConfig();
            config.WithName(AppNames.SlackStatusApp);
            var app = new SlackStatusApp(NullLogger.Instance, config, Address(), new Mock<IAwtrixService>().Object, slack.Object);
            await app.InitAsync();

            await app.DisposeAsync();

            slack.VerifyRemove(s => s.UserStatusChanged -= It.IsAny<EventHandler<SlackUserStatusChangedEventArgs>>(), Times.Once);
        }

        [Fact]
        public async Task ButtonApp_DisposeAsync_DetachesMessageHandler_SoButtonsNoLongerClick()
        {
            var mqtt = new Mock<IMqttConnector>();
            mqtt.Setup(m => m.Subscribe(It.IsAny<string>())).Returns(Task.CompletedTask);
            var app = new ButtonApp(NullLogger.Instance, AppConfig.Empty().WithName(AppNames.ButtonApp), Address(), new Mock<IAwtrixService>().Object, mqtt.Object);
            await app.InitAsync();
            var clicks = 0;
            app.Click += (_, _) => clicks++;

            await app.DisposeAsync();
            mqtt.Raise(m => m.MessageReceived += null,
                new object[] { MqttTestHelpers.CreateReceivedArgs("awtrix/clock1/stats/buttonLeft", "1") });

            Assert.Equal(0, clicks);
            mqtt.VerifyRemove(m => m.MessageReceived -= It.IsAny<Func<MqttApplicationMessageReceivedEventArgs, Task>>(), Times.Once);
        }

        [Fact]
        public async Task TripTimerApp_Dispose_WhileActive_DoesNotBlockOnSlowAppClear()
        {
            // WS1 deferred (e): TripTimerApp.Dispose used to block on DeactivateAsync -> AppClear
            var awtrix = new Mock<IAwtrixService>();
            var clearIsSlow = false;
            var slowClear = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>()))
                .Returns(() => clearIsSlow ? slowClear.Task : Task.FromResult(true));
            var planner = new Mock<ITripPlannerService>();
            planner.Setup(p => p.GetNextDepartures(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new List<TripSummary>());
            var config = new TripTimerAppConfig
            {
                CronSchedule = "10 6 * * 1-5",
                ActiveTime = TimeSpan.FromMinutes(30),
                TimeToOrigin = TimeSpan.Zero,
                TimeToPrepare = TimeSpan.Zero,
            };
            config.Type = AppNames.TripTimerApp;
            var app = new TripTimerApp(NullLogger.Instance, new MockClock(Noon), Address(), awtrix.Object,
                new Mock<ITimerService>().Object, config, planner.Object);
            app.ExecuteNow();
            clearIsSlow = true;

            try
            {
                await Task.Run(() => app.Dispose()).WaitAsync(Guard);
            }
            finally
            {
                slowClear.TrySetResult(true);
            }
        }
    }
}
```

In `test/Test/Apps/ScheduledAppTests.cs`:
- In `Dispose_CancelsPendingWorkAndClearsApp`, replace `_mockAwtrixService.Verify(x => x.Dismiss(_address), Times.AtLeastOnce);` with `_mockAwtrixService.Verify(x => x.Dismiss(It.IsAny<AwtrixAddress>()), Times.Never);`.
- In `DisposeAsync_EndsActiveRunAndAwaitsDismissAndClear`, replace `_mockAwtrixService.Verify(x => x.Dismiss(_address), Times.Once);` with `_mockAwtrixService.Verify(x => x.Dismiss(It.IsAny<AwtrixAddress>()), Times.Never);`. Task 2 rewrites this file, so the test name stays for now.

In `test/Test/HostedServices/ConductorStartupTests.cs`, inside `StartAsync_ScheduledAppWithoutCronSchedule_IsSkippedAndDisposed`, replace:

```csharp
            awtrix.Verify(a => a.Dismiss(device), Times.Once);
```

with:

```csharp
            // Disposed: InitAsync's clear + the dispose clear. Dispose no longer dismisses other apps' notifications (CR-31).
            awtrix.Verify(a => a.AppClear(device, AppNames.MqttRenderApp), Times.Exactly(2));
            awtrix.Verify(a => a.Dismiss(It.IsAny<AwtrixAddress>()), Times.Never);
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps.AwtrixAppTests|FullyQualifiedName~Test.Apps.AppDisposalTests|FullyQualifiedName~Test.Apps.ScheduledAppTests|FullyQualifiedName~ConductorStartupTests"`
Expected: build FAILS with `CS0115: 'TestAwtrixApp.ReleaseResources()': no suitable method found to override`.

- [ ] **Step 3: Implement the pattern in `AwtrixApp`**

In `src/api/Apps/AwtrixApp.cs`:
1. Add the field `private int _disposeState;` directly below `private int _initState;`.
2. Replace both existing members, `public void Dispose()` (body `AppClear().Wait();`) and WS3's `public virtual async ValueTask DisposeAsync()` including its doc comment, with:

```csharp
        /// <summary>
        /// Conductor's shutdown path. Runs once: <see cref="ReleaseResources"/> (synchronous: cancel work,
        /// unsubscribe), then awaits <see cref="DisposeCoreAsync"/> (final clears). Later calls return at once.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (!TryBeginDispose())
            {
                return;
            }

            ReleaseResources();
            await DisposeCoreAsync();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// For callers that cannot await (tests, using blocks). Runs once and never blocks on the network:
        /// the final clears are started through FireAndLog and not awaited. Conductor uses DisposeAsync.
        /// </summary>
        public void Dispose()
        {
            if (!TryBeginDispose())
            {
                return;
            }

            ReleaseResources();
            _ = FireAndLog(DisposeCoreAsync, nameof(Dispose));
            GC.SuppressFinalize(this);
        }

        protected bool IsDisposed => Volatile.Read(ref _disposeState) != 0;

        /// <summary>
        /// Synchronous, non-blocking release: cancel background work and unsubscribe from events.
        /// Called at most once. Overrides call base.
        /// </summary>
        protected virtual void ReleaseResources()
        {
        }

        /// <summary>
        /// Final publishes after <see cref="ReleaseResources"/>. The default clears this app's custom slot.
        /// Never dismisses notifications: that would remove other apps' notifications too (CR-31).
        /// Overrides call base last.
        /// </summary>
        protected virtual Task DisposeCoreAsync() => AppClear();

        private bool TryBeginDispose() => Interlocked.Exchange(ref _disposeState, 1) == 0;
```

- [ ] **Step 4: Implement the subclass overrides**

In `src/api/Apps/ScheduledApp.cs`:
1. Change `public abstract class ScheduledApp<TConfig> : AwtrixApp<TConfig>, IDisposable where TConfig : ScheduledAppConfig` to `public abstract class ScheduledApp<TConfig> : AwtrixApp<TConfig> where TConfig : ScheduledAppConfig`.
2. Delete `public void Dispose()`, `protected void Dispose(bool disposing)` and `public override async ValueTask DisposeAsync()` (with their doc comments).
3. Add in their place (interim; Task 2 replaces the file):

```csharp
        /// <summary>
        /// Ends the pending wait or active run; the run's teardown continues in the background. Never blocks.
        /// </summary>
        protected override void ReleaseResources()
        {
            Logger.LogInformation("Disposing app {App}", Config.Name);

            CancellationTokenSource? current;
            lock (_ctsLock)
            {
                _disposed = true;
                current = _cts;
                _cts = null!;
            }
            CancelAndDispose(current);

            base.ReleaseResources();
        }
```

In `src/api/Apps/TripTimer/TripTimerApp.cs`, delete both members: `new protected void Dispose(bool disposing) { … }` and `public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }`. The run's `finally` → `DeactivateAsync` already unsubscribes and clears.

In `src/api/Apps/Buttons/ButtonApp.cs`, add directly after the `Initialize` method:

```csharp
        protected override void ReleaseResources()
        {
            _mqttConnector.MessageReceived -= RawMessageReceived;
            base.ReleaseResources();
        }
```

In `src/api/Apps/Diurnal/DiurnalApp.cs`, add directly after the `Initialize` method:

```csharp
        protected override void ReleaseResources()
        {
            _timerService.MinuteChanged -= ClockTickMinute;
            base.ReleaseResources();
        }
```

In `src/api/Apps/SlackStatus/SlackStatusApp.cs`, add directly after the `Initialize` method:

```csharp
        protected override void ReleaseResources()
        {
            _slackConnector.UserStatusChanged -= UserStatusChanged;
            base.ReleaseResources();
        }
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps.AwtrixAppTests|FullyQualifiedName~Test.Apps.AppDisposalTests|FullyQualifiedName~Test.Apps.ScheduledAppTests|FullyQualifiedName~ConductorStartupTests"`
Expected: PASS, 0 failed.

- [ ] **Step 6: Run the full suite and code checks**

Run: `dotnet test`
Expected: 0 failed.

Run each check. The expected result follows each arrow.
- `grep -n "new protected\|public void Dispose" src/api/Apps/TripTimer/TripTimerApp.cs src/api/Apps/ScheduledApp.cs` → no matches.
- `grep -rn "await Dismiss()\|_ = Dismiss()" src/api/Apps` → no matches.
- `grep -rn "\.Wait()" src/api/Apps` → no matches.

- [ ] **Step 7: Commit**

```bash
git add src/api/Apps/AwtrixApp.cs src/api/Apps/ScheduledApp.cs src/api/Apps/TripTimer/TripTimerApp.cs src/api/Apps/Buttons/ButtonApp.cs src/api/Apps/Diurnal/DiurnalApp.cs src/api/Apps/SlackStatus/SlackStatusApp.cs test/Test/Apps/AwtrixAppTests.cs test/Test/Apps/AppDisposalTests.cs test/Test/Apps/ScheduledAppTests.cs test/Test/HostedServices/ConductorStartupTests.cs
git commit -m "fix(apps): one idempotent, non-blocking dispose pattern; apps unsubscribe (CR-31)

AwtrixApp.DisposeAsync/Dispose run once via ReleaseResources (sync) and
DisposeCoreAsync (final AppClear). Sync Dispose no longer blocks. Dispose no
longer dismisses other apps' notifications. TripTimerApp's hiding Dispose
members are gone. ButtonApp, DiurnalApp and SlackStatusApp detach their event
handlers. NOTE for WS5: keep the ReleaseResources overrides in DiurnalApp and
SlackStatusApp when rewriting those files.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 2: ScheduledApp activation engine (CR-08, CR-18, WS1 deferred b/c)

**Files:**
- Create: `src/api/Apps/CancellationScope.cs`
- Create: `src/api/Apps/ScheduledActivation.cs`
- Modify: `src/api/Interfaces/IClock.cs` (full replace)
- Modify: `src/api/Domain/Clock.cs` (add `TimeProvider`)
- Modify: `src/api/Apps/ScheduledApp.cs` (full replace)
- Modify: `src/api/Apps/TripTimer/TripTimerApp.cs` (hook migration, behaviour preserved; Task 4 finishes it)
- Modify: `src/api/Apps/MqttRender/MqttRenderApp.cs`, `src/api/Apps/MqttRender/MqttClockRenderApp.cs` (hook migration, behaviour preserved; Task 3 finishes them)
- Modify: `test/Test/Apps/ScheduledAppTests.cs` (full replace)
- Modify: `test/Test/Apps/TripTimer/TripTimerAppTickTests.cs` (full replace, interim)
- Modify: `test/Test/Apps/TripTimer/TripTimerAppBoundaryTests.cs` (remove `_cts` seeding)

**Interfaces:**
- Consumes (Task 1): `AwtrixApp.ReleaseResources()`, `AwtrixApp.DisposeCoreAsync()`.
- Produces:
  - `TimeProvider IClock.TimeProvider` (default interface member → `TimeProvider.System`); `Clock.TimeProvider`
  - `internal sealed class CancellationScope(CancellationToken parent, TimeSpan? timeout = null, TimeProvider? timeProvider = null) : IDisposable`, with `Token`, `IsCancellationRequested`, `Cancel()`, `static ClampTimeout(TimeSpan)` and `static MaxTimeout`
  - `public enum ActivationTrigger { Cron, Manual }`
  - `public sealed class ScheduledActivation`:
    - public: `Number`, `Trigger`, `StartedAt`, `ActiveTime`, `Token`, `IsEnded`, `Complete()`
    - internal: `WasActivated`, `Ended`, `Release()`
  - On `ScheduledApp<TConfig>`:
    - `protected abstract Task OnActivateAsync(ScheduledActivation)`
    - `protected virtual Task OnDeactivateAsync(ScheduledActivation)`
    - `protected ScheduledActivation? CurrentActivation`
    - `internal DateTimeOffset? NextWakeUp`
    - `internal Task LastRun`
    - `internal static TimeSpan NextDelayChunk(TimeSpan)`
    - `internal static readonly TimeSpan MaxDelayChunk`, `WaitRetryDelay`, `DeactivationTimeout`
- Removes: `ActivateScheduledWork`, `WaitForCancellation`, `_cts`, `_ctsLock`, `IsScheduled`.

- [ ] **Step 1: Write the failing tests**

Replace the whole of `test/Test/Apps/ScheduledAppTests.cs`:

```csharp
using System.Threading.Channels;
using AwtrixSharpWeb.Apps;
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Test.Apps
{
    /// <summary>
    /// Concrete ScheduledApp for engine tests. Records activate/deactivate order and publishes each call on a
    /// channel so tests await it instead of sleeping.
    /// </summary>
    internal class TestScheduledApp : ScheduledApp<ScheduledAppConfig>
    {
        private readonly Channel<ScheduledActivation> _activated = Channel.CreateUnbounded<ScheduledActivation>();
        private readonly Channel<ScheduledActivation> _deactivating = Channel.CreateUnbounded<ScheduledActivation>();
        private readonly List<string> _events = new();

        public TestScheduledApp(ILogger logger, IClock clock, AwtrixAddress awtrixAddress, IAwtrixService awtrixService, ScheduledAppConfig config)
            : base(logger, clock, awtrixAddress, awtrixService, config)
        {
        }

        /// <summary>Optional work awaited inside OnActivateAsync (e.g. a gated trip-planner call).</summary>
        public Func<ScheduledActivation, Task>? ActivateWork { get; set; }

        /// <summary>Optional work awaited inside OnDeactivateAsync (e.g. slow teardown).</summary>
        public Func<ScheduledActivation, Task>? DeactivateWork { get; set; }

        public ChannelReader<ScheduledActivation> Activated => _activated.Reader;

        public ChannelReader<ScheduledActivation> Deactivating => _deactivating.Reader;

        public IReadOnlyList<string> Events
        {
            get { lock (_events) { return _events.ToList(); } }
        }

        protected override async Task OnActivateAsync(ScheduledActivation activation)
        {
            lock (_events) { _events.Add($"activate#{activation.Number}"); }
            _activated.Writer.TryWrite(activation);
            if (ActivateWork != null)
            {
                await ActivateWork(activation);
            }
        }

        protected override async Task OnDeactivateAsync(ScheduledActivation activation)
        {
            lock (_events) { _events.Add($"deactivate#{activation.Number}"); }
            _deactivating.Writer.TryWrite(activation);
            if (DeactivateWork != null)
            {
                await DeactivateWork(activation);
            }
        }
    }

    /// <summary>
    /// Wraps a FakeTimeProvider; its LocalTimeZone throws for the first <c>failures</c> reads, which makes
    /// the first next-occurrence computation fail.
    /// </summary>
    internal sealed class FlakyTimeZoneProvider : TimeProvider
    {
        private readonly FakeTimeProvider _inner;
        private int _failuresLeft;

        public FlakyTimeZoneProvider(FakeTimeProvider inner, int failures)
        {
            _inner = inner;
            _failuresLeft = failures;
        }

        public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();

        public override TimeZoneInfo LocalTimeZone =>
            Interlocked.Decrement(ref _failuresLeft) >= 0 ? throw new InvalidOperationException("time zone unavailable") : _inner.LocalTimeZone;

        public override long TimestampFrequency => _inner.TimestampFrequency;

        public override long GetTimestamp() => _inner.GetTimestamp();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            _inner.CreateTimer(callback, state, dueTime, period);
    }

    public class ScheduledAppTests
    {
        private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ActiveTime = TimeSpan.FromMinutes(5);

        // FakeTimeProvider's local zone is UTC, so cron "0 8 * * *" is due one minute after Start.
        private static readonly DateTimeOffset Start = new(2026, 9, 13, 7, 59, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset Eight = Start.AddMinutes(1);

        private readonly AwtrixAddress _address = new() { BaseTopic = "test/base/topic" };
        private FakeTimeProvider _time = null!;
        private Mock<ILogger> _logger = null!;
        private Mock<IAwtrixService> _awtrix = null!;

        private TestScheduledApp CreateSut(
            string cron = "0 8 * * *",
            bool setActiveTime = true,
            Func<FakeTimeProvider, TimeProvider>? wrapTime = null)
        {
            _time = new FakeTimeProvider(Start);
            _logger = new Mock<ILogger>();
            _awtrix = new Mock<IAwtrixService>();
            _awtrix.Setup(x => x.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>())).ReturnsAsync(true);

            var config = new ScheduledAppConfig { CronSchedule = cron };
            if (setActiveTime)
            {
                config.ActiveTime = ActiveTime;
            }
            config.WithName("MyApp");

            var clock = new Clock(wrapTime?.Invoke(_time) ?? _time);
            return new TestScheduledApp(_logger.Object, clock, _address, _awtrix.Object, config);
        }

        private static Task<ScheduledActivation> Next(ChannelReader<ScheduledActivation> reader) =>
            reader.ReadAsync().AsTask().WaitAsync(Guard);

        private void VerifyErrorsLogged(Times times) =>
            _logger.Verify(x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times);

        [Fact]
        public async Task InitAsync_WithInvalidCron_Throws()
        {
            var sut = CreateSut(cron: "not a cron expression");

            await Assert.ThrowsAnyAsync<Exception>(() => sut.InitAsync());
        }

        [Fact]
        public async Task InitAsync_ArmsOneWaitForTheNextCronOccurrence()
        {
            var sut = CreateSut();

            await sut.InitAsync();

            Assert.Equal(Eight, sut.NextWakeUp);
            Assert.Empty(sut.Events);
            await sut.DisposeAsync();
        }

        [Fact]
        public async Task CronOccurrence_Activates_EndsAfterActiveTime_ThenRearmsForTomorrow()
        {
            var sut = CreateSut();
            await sut.InitAsync();

            _time.Advance(TimeSpan.FromSeconds(59));
            Assert.Empty(sut.Events); // not due: no timer fired

            _time.Advance(TimeSpan.FromSeconds(1));
            var activation = await Next(sut.Activated);
            Assert.Equal(ActivationTrigger.Cron, activation.Trigger);
            Assert.Null(sut.NextWakeUp); // no wait armed while active

            _time.Advance(ActiveTime);
            await sut.LastRun.WaitAsync(Guard);

            Assert.True(activation.IsEnded);
            Assert.Equal(new[] { "activate#1", "deactivate#1" }, sut.Events);
            Assert.Equal(Eight.AddDays(1), sut.NextWakeUp);
            VerifyErrorsLogged(Times.Never());
            await sut.DisposeAsync();
        }

        [Fact]
        public async Task ExecuteNow_WhileWaiting_RunsForActiveTime_AndCancelsTheWait()
        {
            // CR-18: manual runs get the same ActiveTime limit as cron runs
            var sut = CreateSut();
            await sut.InitAsync();

            sut.ExecuteNow();

            var activation = await Next(sut.Activated);
            Assert.Equal(ActivationTrigger.Manual, activation.Trigger);
            Assert.Null(sut.NextWakeUp);

            _time.Advance(ActiveTime - TimeSpan.FromSeconds(1)); // passes 08:00: the cancelled wait must not fire
            Assert.False(activation.IsEnded);

            _time.Advance(TimeSpan.FromSeconds(1));
            await sut.LastRun.WaitAsync(Guard);

            Assert.True(activation.IsEnded);
            Assert.Equal(new[] { "activate#1", "deactivate#1" }, sut.Events);
            Assert.Equal(Eight.AddDays(1), sut.NextWakeUp);
            await sut.DisposeAsync();
        }

        [Fact]
        public async Task ExecuteNow_DuringActiveRun_TearsDownOldRunBeforeStartingNew_AndLeavesOneWait()
        {
            // CR-08 scenario A + WS1 deferred (b): the old teardown completes before the new activation wires up,
            // so a late teardown can never remove the new activation's handlers.
            var sut = CreateSut();
            await sut.InitAsync();
            var slowTeardown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            sut.DeactivateWork = a => a.Number == 1 ? slowTeardown.Task : Task.CompletedTask;

            sut.ExecuteNow();
            var first = await Next(sut.Activated);

            sut.ExecuteNow();
            Assert.True(first.IsEnded);
            await Next(sut.Deactivating); // #1 is parked in its teardown
            Assert.Equal(new[] { "activate#1", "deactivate#1" }, sut.Events);

            slowTeardown.SetResult();
            var second = await Next(sut.Activated);

            Assert.Equal(2, second.Number);
            Assert.False(second.IsEnded);
            Assert.Equal(new[] { "activate#1", "deactivate#1", "activate#2" }, sut.Events);
            Assert.Null(sut.NextWakeUp);

            _time.Advance(ActiveTime);
            await sut.LastRun.WaitAsync(Guard);

            Assert.Equal(Eight.AddDays(1), sut.NextWakeUp);
            VerifyErrorsLogged(Times.Never());
            await sut.DisposeAsync();
        }

        [Fact]
        public async Task ExecuteNow_TwiceWhileFirstActivationIsStillStarting_SecondRuns_AndNoErrorIsLogged()
        {
            // CR-08 scenario B + WS1 deferred (c): the superseded activation reads its token after being superseded
            var sut = CreateSut();
            var plannerCall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            sut.ActivateWork = async a =>
            {
                if (a.Number == 1)
                {
                    await plannerCall.Task;                 // e.g. the TfNSW request still in flight
                    a.Token.ThrowIfCancellationRequested(); // OperationCanceledException, never ObjectDisposedException
                }
            };

            sut.ExecuteNow();
            var first = await Next(sut.Activated);
            sut.ExecuteNow();
            Assert.True(first.IsEnded);

            plannerCall.SetResult();
            var second = await Next(sut.Activated);

            Assert.False(second.IsEnded);
            Assert.Equal(new[] { "activate#1", "deactivate#1", "activate#2" }, sut.Events);
            VerifyErrorsLogged(Times.Never());
            await sut.DisposeAsync();
        }

        [Fact]
        public async Task DisposeAsync_DuringActiveRun_DeactivatesOnce_AndNeverRearmsOrReactivates()
        {
            // CR-08 scenario C
            var sut = CreateSut();
            await sut.InitAsync();
            sut.ExecuteNow();
            var activation = await Next(sut.Activated);

            await sut.DisposeAsync();

            Assert.True(activation.IsEnded);
            Assert.Equal(new[] { "activate#1", "deactivate#1" }, sut.Events);
            Assert.Null(sut.NextWakeUp);

            sut.ExecuteNow();
            _time.Advance(TimeSpan.FromDays(2));

            Assert.Equal(new[] { "activate#1", "deactivate#1" }, sut.Events);
            Assert.True(sut.LastRun.IsCompleted);
            _awtrix.Verify(x => x.Dismiss(It.IsAny<AwtrixAddress>()), Times.Never);
            VerifyErrorsLogged(Times.Never());
        }

        [Fact]
        public async Task DisposeAsync_WhenTeardownHangs_CompletesAfterDeactivationTimeout()
        {
            var sut = CreateSut();
            sut.DeactivateWork = _ => new TaskCompletionSource().Task;
            sut.ExecuteNow();
            await Next(sut.Activated);

            var dispose = sut.DisposeAsync().AsTask();
            await Next(sut.Deactivating);
            Assert.False(dispose.IsCompleted);

            _time.Advance(ScheduledApp<ScheduledAppConfig>.DeactivationTimeout);

            await dispose.WaitAsync(Guard);
            _awtrix.Verify(x => x.AppClear(_address, "MyApp"), Times.AtLeast(2)); // activation start + final clear
        }

        [Fact]
        public async Task CronDelayLongerThan49Days_StillActivates()
        {
            // Task.Delay rejects delays over ~49.7 days; a yearly cron used to fail once and never run
            var sut = CreateSut(cron: "0 0 1 1 *");
            await sut.InitAsync();
            var due = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
            Assert.Equal(due, sut.NextWakeUp);

            _time.Advance(due - Start);

            // Each 1-day chunk re-arms on a pool continuation; pump due timers until the activation arrives.
            var deadline = DateTime.UtcNow + Guard;
            ScheduledActivation? activation;
            while (!sut.Activated.TryRead(out activation))
            {
                Assert.True(DateTime.UtcNow < deadline, "yearly cron did not activate");
                _time.Advance(TimeSpan.Zero);
                await Task.Yield();
            }

            Assert.Equal(ActivationTrigger.Cron, activation!.Trigger);
            VerifyErrorsLogged(Times.Never());
            await sut.DisposeAsync();
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(24 * 60, 24 * 60)]
        [InlineData(200 * 24 * 60, 24 * 60)]
        public void NextDelayChunk_NeverExceedsOneDay(int remainingMinutes, int expectedMinutes)
        {
            Assert.Equal(
                TimeSpan.FromMinutes(expectedMinutes),
                ScheduledApp<ScheduledAppConfig>.NextDelayChunk(TimeSpan.FromMinutes(remainingMinutes)));
        }

        [Fact]
        public async Task WaitFailure_IsLoggedAndRetried_InsteadOfNeverRescheduling()
        {
            var sut = CreateSut(wrapTime: fake => new FlakyTimeZoneProvider(fake, failures: 1));

            await sut.InitAsync(); // the first next-occurrence computation throws

            Assert.Null(sut.NextWakeUp);
            VerifyErrorsLogged(Times.Once());

            _time.Advance(ScheduledApp<ScheduledAppConfig>.WaitRetryDelay);

            Assert.True(SpinWait.SpinUntil(() => sut.NextWakeUp != null, Guard));
            Assert.Equal(Eight.AddDays(1), sut.NextWakeUp); // retried at 08:00, so the next occurrence is tomorrow
            await sut.DisposeAsync();
        }

        [Fact]
        public async Task ExecuteNow_WithMissingActiveTime_LogsErrorAndDoesNotActivate_ButKeepsScheduling()
        {
            var sut = CreateSut(setActiveTime: false);
            await sut.InitAsync();

            sut.ExecuteNow();

            Assert.Empty(sut.Events);
            Assert.True(sut.LastRun.IsCompleted);
            Assert.Equal(Eight, sut.NextWakeUp);
            VerifyErrorsLogged(Times.Once());
            await sut.DisposeAsync();
        }
    }
}
```

Replace the whole of `test/Test/Apps/TripTimer/TripTimerAppTickTests.cs` (interim; Task 4 replaces it):

```csharp
using System.Reflection;
using AwtrixSharpWeb.Apps.TripTimer;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Test.Domain;

namespace Test.Apps.TripTimer
{
    /// <summary>
    /// ClockTickSecond runs on the timer loop; it must never let an exception escape or wait on teardown I/O.
    /// </summary>
    public class TripTimerAppTickTests
    {
        private static readonly DateTimeOffset Departure = DateTimeOffset.Parse("2025-08-19T06:41:00+10:00");

        private static (TripTimerApp app, Mock<IAwtrixService> awtrix) Create(DateTimeOffset now)
        {
            var awtrix = new Mock<IAwtrixService>();
            awtrix.Setup(a => a.Notify(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixAppMessage>())).ReturnsAsync(true);
            awtrix.Setup(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>())).ReturnsAsync(true);
            awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>())).ReturnsAsync(true);
            var planner = new Mock<ITripPlannerService>();
            planner.Setup(p => p.GetNextDepartures(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new List<TripSummary> { TripSummaryTests.Create(Departure) });
            var config = new TripTimerAppConfig
            {
                CronSchedule = "* * * * *",
                ActiveTime = TimeSpan.FromMinutes(30),
                TimeToOrigin = TimeSpan.Zero,
                TimeToPrepare = TimeSpan.Zero,
            };
            config.Type = AppNames.TripTimerApp;

            var app = new TripTimerApp(
                NullLogger.Instance,
                new MockClock(now),
                new AwtrixAddress { BaseTopic = "awtrix/clock1" },
                awtrix.Object,
                new Mock<ITimerService>().Object,
                config,
                planner.Object);

            app.NextDepartures.Add(TripSummaryTests.Create(Departure));
            return (app, awtrix);
        }

        private static void InvokeClockTickSecond(TripTimerApp app, DateTime time)
        {
            var method = typeof(TripTimerApp).GetMethod("ClockTickSecond", BindingFlags.NonPublic | BindingFlags.Instance);
            method!.Invoke(app, new object?[] { null, new ClockTickEventArgs(time) });
        }

        [Fact]
        public void SecondTick_WhenAppUpdateFaults_DoesNotPropagate()
        {
            var now = Departure.AddMinutes(-3);
            var (app, awtrix) = Create(now);
            awtrix.Setup(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()))
                .ThrowsAsync(new HttpRequestException("device offline"));

            var exception = Record.Exception(() => InvokeClockTickSecond(app, now.DateTime));

            Assert.Null(exception);
        }

        [Fact]
        public void SecondTick_NoFutureDeparturesWhileNotActive_DoesNotPropagate()
        {
            var now = Departure.AddMinutes(5);
            var (app, _) = Create(now);

            var exception = Record.Exception(() => InvokeClockTickSecond(app, now.DateTime));

            Assert.Null(exception);
        }

        [Fact]
        public async Task SecondTick_NoFutureDeparturesWhileActive_ReturnsPromptlyWhenAppClearIsSlow()
        {
            var now = Departure.AddMinutes(5);
            var (app, awtrix) = Create(now);
            var clearIsSlow = false;
            var slowClear = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>()))
                .Returns(() => clearIsSlow ? slowClear.Task : Task.FromResult(true));
            app.ExecuteNow();
            clearIsSlow = true;

            try
            {
                await Task.Run(() => InvokeClockTickSecond(app, now.DateTime)).WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                slowClear.TrySetResult(true);
            }

            await app.LastRun.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
```

In `test/Test/Apps/TripTimer/TripTimerAppBoundaryTests.cs`, delete these lines from `GetSystemUnderTest` (the field no longer exists):

```csharp
            // BuildMessage cancels `_cts` when there are no future departures; ActivateScheduledWork
            // (which normally creates it) is never invoked in these unit tests, so it must be seeded
            // via reflection to avoid a NullReferenceException on that code path.
            var ctsField = typeof(TripTimerApp).GetField("_cts", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            ctsField.SetValue(sut, new System.Threading.CancellationTokenSource());

```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps.ScheduledAppTests|FullyQualifiedName~Test.Apps.TripTimer"`
Expected: build FAILS with `CS0246: The type or namespace name 'ScheduledActivation' could not be found` and `CS1061: 'TestScheduledApp' does not contain a definition for 'LastRun'`.

- [ ] **Step 3: Implement the time source**

Replace the whole of `src/api/Interfaces/IClock.cs`:

```csharp
namespace AwtrixSharpWeb.Interfaces
{
    public interface IClock
    {
        DateTimeOffset Now { get; }

        /// <summary>
        /// Time source for timers and delays driven by this clock (cron waits, ActiveTime).
        /// Test clocks that only fake <see cref="Now"/> get the system provider.
        /// </summary>
        TimeProvider TimeProvider => TimeProvider.System;
    }
}
```

In `src/api/Domain/Clock.cs`, add below `public DateTimeOffset Now => _timeProvider.GetLocalNow();`:

```csharp

        public TimeProvider TimeProvider => _timeProvider;
```

- [ ] **Step 4: Implement `CancellationScope` and `ScheduledActivation`**

Create `src/api/Apps/CancellationScope.cs`:

```csharp
namespace AwtrixSharpWeb.Apps
{
    /// <summary>
    /// A CancellationTokenSource linked to a parent token, with an optional timeout on an injected TimeProvider.
    /// <see cref="Token"/> is captured at construction, so reading it never throws ObjectDisposedException, and
    /// <see cref="Cancel"/> is a no-op after <see cref="Dispose"/>, so a late canceller never throws either.
    /// </summary>
    internal sealed class CancellationScope : IDisposable
    {
        /// <summary>Largest delay a CancellationTokenSource timer accepts (about 49.7 days).</summary>
        internal static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

        private readonly object _sync = new();
        private readonly CancellationTokenSource _linked;
        private readonly CancellationTokenSource? _timeout;
        private bool _disposed;

        public CancellationScope(CancellationToken parent, TimeSpan? timeout = null, TimeProvider? timeProvider = null)
        {
            if (timeout is { } requested)
            {
                _timeout = new CancellationTokenSource(ClampTimeout(requested), timeProvider ?? TimeProvider.System);
                _linked = CancellationTokenSource.CreateLinkedTokenSource(parent, _timeout.Token);
            }
            else
            {
                _linked = CancellationTokenSource.CreateLinkedTokenSource(parent);
            }

            Token = _linked.Token;
        }

        public CancellationToken Token { get; }

        public bool IsCancellationRequested => Token.IsCancellationRequested;

        public void Cancel()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }
                _linked.Cancel();
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
            }
            _linked.Dispose();
            _timeout?.Dispose();
        }

        internal static TimeSpan ClampTimeout(TimeSpan requested) =>
            requested <= TimeSpan.Zero ? TimeSpan.Zero : requested > MaxTimeout ? MaxTimeout : requested;
    }
}
```

Create `src/api/Apps/ScheduledActivation.cs`:

```csharp
namespace AwtrixSharpWeb.Apps
{
    public enum ActivationTrigger
    {
        Cron,
        Manual
    }

    /// <summary>
    /// One window during which a ScheduledApp owns the clock. Its <see cref="Token"/> is cancelled when the app
    /// calls <see cref="Complete"/>, when ActiveTime elapses, when a newer activation supersedes it, or when the
    /// app is disposed. Background work started for the window (e.g. a refresh loop) should observe the token.
    /// </summary>
    public sealed class ScheduledActivation
    {
        private readonly CancellationScope _scope;
        private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal ScheduledActivation(int number, ActivationTrigger trigger, DateTimeOffset startedAt, TimeSpan activeTime, TimeProvider timeProvider, CancellationToken lifetime)
        {
            Number = number;
            Trigger = trigger;
            StartedAt = startedAt;
            ActiveTime = activeTime;
            _scope = new CancellationScope(lifetime, activeTime, timeProvider);
            Token = _scope.Token;
            Token.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), _ended);
        }

        /// <summary>1-based sequence number within the app.</summary>
        public int Number { get; }

        public ActivationTrigger Trigger { get; }

        public DateTimeOffset StartedAt { get; }

        public TimeSpan ActiveTime { get; }

        public CancellationToken Token { get; }

        public bool IsEnded => Token.IsCancellationRequested;

        /// <summary>
        /// End this window early (e.g. nothing left to show). Idempotent, thread-safe, never throws, and never runs
        /// deactivation inline on the caller's thread.
        /// </summary>
        public void Complete() => _scope.Cancel();

        /// <summary>True once OnActivateAsync was called; only then does deactivation run.</summary>
        internal bool WasActivated { get; set; }

        /// <summary>Completes (asynchronously) when <see cref="Token"/> is cancelled. Never faults.</summary>
        internal Task Ended => _ended.Task;

        internal void Release() => _scope.Dispose();
    }
}
```

- [ ] **Step 5: Implement the engine (replace the whole of `src/api/Apps/ScheduledApp.cs`)**

```csharp
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using NCrontab;

namespace AwtrixSharpWeb.Apps
{
    /// <summary>
    /// Base for apps that take over the clock for Config.ActiveTime, on a cron schedule or on demand (ExecuteNow).
    /// <para>
    /// State, all mutated under <see cref="_gate"/>: at most one pending cron wait and at most one current
    /// activation; nothing is armed while an activation is current; nothing re-arms or activates after disposal.
    /// Activations are serialised: a superseded activation's teardown (OnDeactivateAsync + AppClear) completes
    /// before the next activation's OnActivateAsync runs.
    /// </para>
    /// </summary>
    public abstract class ScheduledApp<TConfig> : AwtrixApp<TConfig> where TConfig : ScheduledAppConfig
    {
        /// <summary>Longest single timer used while waiting for a cron occurrence (Task.Delay rejects > ~49.7 days).</summary>
        internal static readonly TimeSpan MaxDelayChunk = TimeSpan.FromDays(1);

        /// <summary>Back-off before retrying after a failure while waiting for the schedule.</summary>
        internal static readonly TimeSpan WaitRetryDelay = TimeSpan.FromMinutes(1);

        /// <summary>Budget for OnDeactivateAsync, and for disposal waiting on the last run's teardown.</summary>
        internal static readonly TimeSpan DeactivationTimeout = TimeSpan.FromSeconds(5);

        private readonly object _gate = new();
        private readonly CancellationTokenSource _lifetime = new(); // cancelled on dispose; never disposed (no timer)
        private CancellationScope? _pendingWait;
        private ScheduledActivation? _active;
        private ScheduledActivation? _currentActivation;
        private Task _lastRun = Task.CompletedTask;
        private DateTimeOffset? _nextWakeUp;
        private int _activationCount;
        private bool _disposed;

        public ScheduledApp(ILogger logger, IClock clock, AwtrixAddress awtrixAddress, IAwtrixService awtrixService, TConfig config)
            : base(logger, config, awtrixAddress, awtrixService)
        {
            Clock = clock;
        }

        protected IClock Clock { get; }

        protected CrontabSchedule? CrontabSchedule { get; private set; }

        /// <summary>
        /// The activation whose OnActivateAsync ran and whose OnDeactivateAsync has not finished, or null.
        /// Event handlers guard with <c>if (CurrentActivation is not { IsEnded: false }) return;</c>.
        /// </summary>
        protected ScheduledActivation? CurrentActivation => Volatile.Read(ref _currentActivation);

        /// <summary>When the pending cron wait will activate the app; null while active, unscheduled or disposed.</summary>
        internal DateTimeOffset? NextWakeUp
        {
            get { lock (_gate) { return _nextWakeUp; } }
        }

        /// <summary>Completes when the latest activation's teardown and re-arm have finished. Never faults.</summary>
        internal Task LastRun
        {
            get { lock (_gate) { return _lastRun; } }
        }

        private TimeProvider Time => Clock.TimeProvider;

        /// <summary>
        /// Wire up the window (subscribe, fetch) and return once wired; the base waits for the window to end.
        /// Observe <see cref="ScheduledActivation.Token"/> in anything that can take a while.
        /// </summary>
        protected abstract Task OnActivateAsync(ScheduledActivation activation);

        /// <summary>
        /// Undo OnActivateAsync (unsubscribe). Runs once per activation that was activated, even if activation threw.
        /// The base clears the app slot afterwards. Bounded by <see cref="DeactivationTimeout"/>.
        /// </summary>
        protected virtual Task OnDeactivateAsync(ScheduledActivation activation) => Task.CompletedTask;

        internal static TimeSpan NextDelayChunk(TimeSpan remaining) => remaining < MaxDelayChunk ? remaining : MaxDelayChunk;

        protected override void Initialize()
        {
            CrontabSchedule = CrontabSchedule.Parse(Config.CronSchedule);
            ArmNextWait();
        }

        public override void ExecuteNow()
        {
            Logger.LogInformation("ExecuteNow: activating {App} on {AwtrixAddress}", Config.Name, AwtrixAddress);
            StartActivation(ActivationTrigger.Manual, fromWait: null);
        }

        private void ArmNextWait()
        {
            CancellationScope wait;
            lock (_gate)
            {
                if (_disposed || CrontabSchedule == null || _active != null || _pendingWait != null)
                {
                    return;
                }
                wait = new CancellationScope(_lifetime.Token);
                _pendingWait = wait;
            }

            // Runs synchronously up to its first timer, so the wait is armed when this returns.
            _ = WaitThenActivateAsync(wait);
        }

        private async Task WaitThenActivateAsync(CancellationScope wait)
        {
            try
            {
                while (true)
                {
                    try
                    {
                        var due = GetNextOccurrence();
                        lock (_gate)
                        {
                            if (ReferenceEquals(_pendingWait, wait))
                            {
                                _nextWakeUp = due;
                            }
                        }
                        Logger.LogInformation("{App} next wake up scheduled for {Due} (in {Delay})", Config.Name, due, due - Time.GetUtcNow());

                        await DelayUntilAsync(due, wait.Token);
                        break;
                    }
                    catch (OperationCanceledException) when (wait.IsCancellationRequested)
                    {
                        return; // superseded by ExecuteNow, or disposed
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "{App} failed while waiting for its schedule; retrying in {Delay}", Config.Name, WaitRetryDelay);
                        try
                        {
                            await Task.Delay(WaitRetryDelay, Time, wait.Token).ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                    }
                }

                StartActivation(ActivationTrigger.Cron, wait);
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_pendingWait, wait))
                    {
                        _pendingWait = null;
                        _nextWakeUp = null;
                    }
                }
                wait.Dispose();
            }
        }

        /// <summary>Next cron occurrence strictly after now, in the time provider's local zone (host-local in production).</summary>
        private DateTimeOffset GetNextOccurrence()
        {
            var now = Time.GetLocalNow();
            var next = CrontabSchedule!.GetNextOccurrence(now.DateTime);
            return new DateTimeOffset(next, Time.LocalTimeZone.GetUtcOffset(next));
        }

        private async Task DelayUntilAsync(DateTimeOffset due, CancellationToken token)
        {
            while (true)
            {
                var remaining = due - Time.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    return;
                }

                // ForceYielding: a cancelling thread (tick handler, Dispose) never runs this continuation inline
                await Task.Delay(NextDelayChunk(remaining), Time, token).ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            }
        }

        private void StartActivation(ActivationTrigger trigger, CancellationScope? fromWait)
        {
            var activeTime = ReadActiveTime();

            ScheduledActivation activation;
            ScheduledActivation? superseded;
            CancellationScope? pendingWait;
            Task previousRun;
            TaskCompletionSource runCompleted;
            lock (_gate)
            {
                if (_disposed)
                {
                    Logger.LogDebug("{App} is disposed; ignoring {Trigger} activation", Config.Name, trigger);
                    return;
                }
                if (fromWait != null && !ReferenceEquals(_pendingWait, fromWait))
                {
                    return; // this wait was superseded while it was waking up
                }

                superseded = _active;
                pendingWait = _pendingWait;
                _pendingWait = null;
                _nextWakeUp = null;

                activation = new ScheduledActivation(++_activationCount, trigger, Time.GetLocalNow(), activeTime, Time, _lifetime.Token);
                _active = activation;

                previousRun = _lastRun;
                runCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _lastRun = runCompleted.Task;
            }

            if (pendingWait != null && !ReferenceEquals(pendingWait, fromWait))
            {
                pendingWait.Cancel();
            }
            if (superseded != null)
            {
                Logger.LogInformation("{App} activation #{Old} superseded by #{New} ({Trigger})", Config.Name, superseded.Number, activation.Number, trigger);
                superseded.Complete();
            }

            _ = RunActivationAsync(activation, previousRun, runCompleted);
        }

        private TimeSpan ReadActiveTime()
        {
            try
            {
                var activeTime = Config.ActiveTime;
                if (activeTime <= TimeSpan.Zero)
                {
                    Logger.LogWarning("{App} ActiveTime is {ActiveTime}; the activation ends immediately", Config.Name, activeTime);
                }
                return activeTime;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{App} has a missing or invalid ActiveTime; the activation ends immediately", Config.Name);
                return TimeSpan.Zero;
            }
        }

        private async Task RunActivationAsync(ScheduledActivation activation, Task previousRun, TaskCompletionSource runCompleted)
        {
            try
            {
                // Serialise: the previous activation's teardown finishes before this one wires anything up.
                await previousRun;
                if (activation.IsEnded)
                {
                    return; // superseded while waiting, ActiveTime <= 0, or disposed
                }

                Logger.LogInformation("{App} activation #{Number} ({Trigger}) starting for {ActiveTime}", Config.Name, activation.Number, activation.Trigger, activation.ActiveTime);
                activation.WasActivated = true;
                Volatile.Write(ref _currentActivation, activation);

                await AppClear();
                await OnActivateAsync(activation);
                await activation.Ended;
            }
            catch (OperationCanceledException) when (activation.IsEnded)
            {
                Logger.LogDebug("{App} activation #{Number} ended while activating", Config.Name, activation.Number);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{App} activation #{Number} failed", Config.Name, activation.Number);
            }
            finally
            {
                await EndActivationAsync(activation, runCompleted);
            }
        }

        private async Task EndActivationAsync(ScheduledActivation activation, TaskCompletionSource runCompleted)
        {
            activation.Complete(); // OnActivateAsync may have thrown: end the window so its timeout and handlers stop

            if (activation.WasActivated)
            {
                try
                {
                    await OnDeactivateAsync(activation).WaitAsync(DeactivationTimeout, Time);
                }
                catch (TimeoutException)
                {
                    Logger.LogWarning("{App} activation #{Number} did not deactivate within {Timeout}", Config.Name, activation.Number, DeactivationTimeout);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "{App} activation #{Number} failed to deactivate", Config.Name, activation.Number);
                }

                Interlocked.CompareExchange(ref _currentActivation, null, activation);

                try
                {
                    await AppClear();
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "{App} could not clear its slot after activation #{Number}", Config.Name, activation.Number);
                }

                Logger.LogInformation("{App} activation #{Number} ended after {Seconds:F1} s", Config.Name, activation.Number, (Time.GetLocalNow() - activation.StartedAt).TotalSeconds);
            }

            bool rearm;
            lock (_gate)
            {
                rearm = ReferenceEquals(_active, activation);
                if (rearm)
                {
                    _active = null;
                }
                rearm = rearm && !_disposed;
            }

            activation.Release();
            if (rearm)
            {
                ArmNextWait();
            }
            runCompleted.TrySetResult(); // last, so awaiting LastRun also observes the re-armed wait
        }

        protected override void ReleaseResources()
        {
            Logger.LogInformation("Disposing app {App}", Config.Name);
            lock (_gate)
            {
                _disposed = true;
                _nextWakeUp = null;
            }
            _lifetime.Cancel(); // ends the pending wait and the current activation; teardown runs on the thread pool
            base.ReleaseResources();
        }

        protected override async Task DisposeCoreAsync()
        {
            try
            {
                await LastRun.WaitAsync(DeactivationTimeout, Time);
            }
            catch (TimeoutException)
            {
                Logger.LogWarning("{App} did not finish deactivating within {Timeout}; clearing anyway", Config.Name, DeactivationTimeout);
            }

            await base.DisposeCoreAsync();
        }
    }
}
```

- [ ] **Step 6: Migrate the subclasses to the hooks (behaviour preserved)**

In `src/api/Apps/TripTimer/TripTimerApp.cs`:
1. In `BuildMessage`, replace `_cts.Cancel();` with `CurrentActivation?.Complete(); // interim; Task 4 returns null instead`.
2. In `ClockTickSecond`, replace the comment `// BuildMessage runs inside FireAndLog so its _cts.Cancel() cannot escape onto the timer loop` with `// BuildMessage runs inside FireAndLog so nothing can escape onto the timer loop`.
3. Replace everything from `protected override async Task ActivateScheduledWork(CancellationTokenSource cts)` to the end of the `DeactivateAsync` method with:

```csharp
        protected override async Task OnActivateAsync(ScheduledActivation activation)
        {
            Logger.LogInformation("Trip timer activated ({Trigger})", activation.Trigger);

            var message = new AwtrixAppMessage()
                .SetText($"Starting trip timer")
                .SetStack(false);

            await Notify(message);

            // Find the earliest we could get to the train station and query from then
            var earliestDeparture = Clock.Now.Add(Config.TimeToOrigin).Add(Config.TimeToPrepare);

            var newDepartures = await _tripPlanner
                .GetNextDepartures(Config.StopIdOrigin, Config.StopIdDestination, earliestDeparture.LocalDateTime)
                .WaitAsync(activation.Token);
            NextDepartures.Clear();

            foreach (var departure in newDepartures)
            {
                Logger.LogInformation("Raw Departure: {departure}", departure);
            }

            // Round to the minute otherwise we get to alarm time and it isn't aligned to minute boundaries
            NextDepartures.AddRange(newDepartures.Select(d => d.AsRounded()));

            Logger.LogInformation($"{NextDepartures.Count} future departures computed:");
            Logger.LogInformation($"Prep -> Leave -> Departure");
            NextDepartures.ForEach(d => Logger.LogInformation(GetAlarmTime(d).ToString()));

            _timerService.SecondChanged += ClockTickSecond;
            _timerService.MinuteChanged += ClockTickMinute;
        }

        protected override Task OnDeactivateAsync(ScheduledActivation activation)
        {
            Logger.LogInformation($"Schedule deactivating");
            _timerService.SecondChanged -= ClockTickSecond;
            _timerService.MinuteChanged -= ClockTickMinute;
            return Task.CompletedTask;
        }
```

In `src/api/Apps/MqttRender/MqttRenderApp.cs`, replace everything from `protected override async Task ActivateScheduledWork(CancellationTokenSource cts)` to the end of the `private async Task Deactivate()` method with:

```csharp
        protected override async Task OnActivateAsync(ScheduledActivation activation)
        {
            // Attach first: a retained message can arrive before Subscribe returns (CR-33)
            _mqttConnector.MessageReceived += RawMessageReceived;
            await _mqttConnector.Subscribe(Config.ReadTopic);
        }

        protected override Task OnDeactivateAsync(ScheduledActivation activation)
        {
            _mqttConnector.MessageReceived -= RawMessageReceived;
            return Task.CompletedTask;
        }
```

In `src/api/Apps/MqttRender/MqttClockRenderApp.cs`, replace:

```csharp
        protected override async Task ActivateScheduledWork(CancellationTokenSource cts)
        {
            _timerService.SecondChanged += ClockTick;

            await base.ActivateScheduledWork(cts);
        }
```

with (the missing unsubscribe is CR-09, fixed test-first in Task 3):

```csharp
        protected override async Task OnActivateAsync(ScheduledActivation activation)
        {
            _timerService.SecondChanged += ClockTick;

            await base.OnActivateAsync(activation);
        }
```

- [ ] **Step 7: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps.ScheduledAppTests|FullyQualifiedName~Test.Apps.TripTimer|FullyQualifiedName~Test.Apps.MqttRender|FullyQualifiedName~Test.Apps.AppDisposalTests"`
Expected: PASS, 0 failed.

If `CronDelayLongerThan49Days_StillActivates` or `WaitFailure_IsLoggedAndRetried…` fails intermittently, do not add sleeps. Instead check that `DelayUntilAsync` recomputes `remaining` from `Time.GetUtcNow()` on every loop, and that `NextWakeUp` is only set under `_gate`.

- [ ] **Step 8: Run the full suite and code checks**

Run: `dotnet test`
Expected: 0 failed. WS2's retained-message tests and WS3's `StartAsync_RightDoubleClick_StartsOnlyThatDevicesTripTimerOnce` rely on the synchronous activation prefix (spec D3); they must pass unchanged.

Run each check. The expected result follows each arrow.
- `grep -rn "ActivateScheduledWork\|WaitForCancellation\|_ctsLock\|IsScheduled" src test --include=*.cs | grep -v /obj/` → no matches.
- `grep -n "CancellationTokenSource" src/api/Apps/ScheduledApp.cs src/api/Apps/TripTimer/TripTimerApp.cs src/api/Apps/MqttRender/*.cs` → only `private readonly CancellationTokenSource _lifetime` in `ScheduledApp.cs`.
- `grep -n "Task.Delay\|Thread.Sleep" test/Test/Apps/ScheduledAppTests.cs` → no matches.

- [ ] **Step 9: Commit**

```bash
git add src/api/Apps/CancellationScope.cs src/api/Apps/ScheduledActivation.cs src/api/Interfaces/IClock.cs src/api/Domain/Clock.cs src/api/Apps/ScheduledApp.cs src/api/Apps/TripTimer/TripTimerApp.cs src/api/Apps/MqttRender/MqttRenderApp.cs src/api/Apps/MqttRender/MqttClockRenderApp.cs test/Test/Apps/ScheduledAppTests.cs test/Test/Apps/TripTimer/TripTimerAppTickTests.cs test/Test/Apps/TripTimer/TripTimerAppBoundaryTests.cs
git commit -m "fix(scheduler): race-free ScheduledApp activation engine (CR-08, CR-18)

One pending cron wait and one activation, guarded by a lock and an app-lifetime
CTS. Each activation is a ScheduledActivation with its own token and an
ActiveTime timeout for cron and manual runs alike. Activations are serialised,
so an old teardown can never remove a new activation's handlers. Cron waits
are chunked (no 49.7-day limit) and retried after failures. Subclasses use
OnActivateAsync/OnDeactivateAsync and never see a CancellationTokenSource.
IClock exposes its TimeProvider for deterministic tests.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 3: MqttRender apps stop at the end of their window (CR-09)

**Files:**
- Modify: `src/api/Apps/MqttRender/MqttRenderApp.cs` (full replace)
- Modify: `src/api/Apps/MqttRender/MqttClockRenderApp.cs` (full replace)
- Modify: `test/Test/Apps/MqttRender/MqttRenderAppTests.cs` (pinned fake time; three tests)
- Modify: `test/Test/Apps/MqttRender/MqttClockRenderAppTests.cs` (pinned fake time; three tests)

**Interfaces:**
- Consumes (Task 2): `OnActivateAsync`/`OnDeactivateAsync`, `CurrentActivation`, `ScheduledActivation.Token`/`IsEnded`, `LastRun`, `IClock.TimeProvider`.
- Produces: no new members. `MqttRenderApp.OnDeactivateAsync` is `protected override`, and `MqttClockRenderApp` overrides it again and calls `base`.

- [ ] **Step 1: Write the failing tests**

In **both** `test/Test/Apps/MqttRender/MqttRenderAppTests.cs` and `test/Test/Apps/MqttRender/MqttClockRenderAppTests.cs`:
1. Add `using System.Reflection;` and `using Microsoft.Extensions.Time.Testing;` to the usings. Add `using AwtrixSharpWeb.Domain;` if it is missing.
2. Replace the field `private MockClock _clock;` with:

```csharp
        private FakeTimeProvider _time;
        private IClock _clock;
```

3. In `CreateSut`, replace `_clock = new MockClock(DateTimeOffset.Now);` with:

```csharp
            // Pinned instant (CR-39); FakeTimeProvider drives ActiveTime so windows can be ended deterministically
            _time = new FakeTimeProvider(new DateTimeOffset(2025, 1, 1, 8, 0, 0, TimeSpan.Zero));
            _clock = new Clock(_time);
```

Add inside `public class MqttRenderAppTests`, at the end:

```csharp
        private async Task EndWindowAsync(AwtrixSharpWeb.Apps.MqttRender.MqttRenderApp sut)
        {
            _time.Advance(_config.ActiveTime);
            await sut.LastRun.WaitAsync(TimeSpan.FromSeconds(5));
            _mockAwtrixService.Invocations.Clear();
        }

        [Fact]
        public async Task MessageAfterActiveTimeEnds_IsNotRendered()
        {
            var sut = CreateSut("read/topic");
            sut.ExecuteNow();
            await EndWindowAsync(sut);

            _mockMqttConnector.Raise(x => x.MessageReceived += null,
                new object[] { MqttTestHelpers.CreateReceivedArgs("read/topic", "late") });

            _mockAwtrixService.Verify(x => x.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()), Times.Never);
            _mockMqttConnector.VerifyRemove(x => x.MessageReceived -= It.IsAny<Func<MqttApplicationMessageReceivedEventArgs, Task>>(), Times.Once);
        }

        [Fact]
        public async Task MessageDispatchedAfterDeactivation_IsIgnored()
        {
            // The connector snapshots its handler list, so a dispatch in flight can reach a detached handler
            var sut = CreateSut("read/topic");
            sut.ExecuteNow();
            await EndWindowAsync(sut);

            var handler = typeof(AwtrixSharpWeb.Apps.MqttRender.MqttRenderApp)
                .GetMethod("RawMessageReceived", BindingFlags.NonPublic | BindingFlags.Instance)!;
            await (Task)handler.Invoke(sut, new object[] { MqttTestHelpers.CreateReceivedArgs("read/topic", "late") })!;

            _mockAwtrixService.Verify(x => x.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()), Times.Never);
        }

        [Fact]
        public async Task SubscribeThatNeverCompletes_DoesNotOutliveTheWindow()
        {
            var sut = CreateSut("read/topic");
            _mockMqttConnector.Setup(x => x.Subscribe("read/topic")).Returns(new TaskCompletionSource().Task);
            sut.ExecuteNow();

            _time.Advance(_config.ActiveTime);

            await sut.LastRun.WaitAsync(TimeSpan.FromSeconds(5));
            _mockMqttConnector.VerifyRemove(x => x.MessageReceived -= It.IsAny<Func<MqttApplicationMessageReceivedEventArgs, Task>>(), Times.Once);
        }
```

Add inside `public class MqttClockRenderAppTests`, at the end:

```csharp
        private async Task EndWindowAsync(MqttClockRenderApp sut)
        {
            _time.Advance(_config.ActiveTime);
            await sut.LastRun.WaitAsync(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task AfterActiveTimeEnds_ClockTicksNoLongerPublish()
        {
            // CR-09: SecondChanged was never unsubscribed, so the slot was republished a second after being cleared
            var sut = CreateSut();
            sut.ExecuteNow();
            await EndWindowAsync(sut);
            _mockAwtrixService.Invocations.Clear();

            _mockTimerService.Raise(x => x.SecondChanged += null, this, new ClockTickEventArgs(new DateTime(2025, 1, 1, 8, 5, 1)));

            _mockAwtrixService.Verify(x => x.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()), Times.Never);
            _mockTimerService.VerifyRemove(x => x.SecondChanged -= It.IsAny<EventHandler<ClockTickEventArgs>>(), Times.Once);
        }

        [Fact]
        public async Task RepeatedActivations_AttachOnlyOneClockTickHandler()
        {
            // CR-09 failure scenario: after N daily activations the clock received N publishes per second
            var sut = CreateSut();
            for (var day = 0; day < 3; day++)
            {
                sut.ExecuteNow();
                await EndWindowAsync(sut);
            }
            sut.ExecuteNow();
            _mockAwtrixService.Invocations.Clear();

            _mockTimerService.Raise(x => x.SecondChanged += null, this, new ClockTickEventArgs(new DateTime(2025, 1, 1, 8, 20, 1)));

            _mockAwtrixService.Verify(x => x.AppUpdate(_address, "MqttClockRenderApp", It.IsAny<AwtrixAppMessage>()), Times.Once);
            await sut.DisposeAsync();
        }

        [Fact]
        public async Task ClockTickDeliveredAfterDeactivation_IsIgnored()
        {
            // TimerService snapshots its invocation list, so one tick can arrive after unsubscribe
            var sut = CreateSut();
            sut.ExecuteNow();
            await EndWindowAsync(sut);
            _mockAwtrixService.Invocations.Clear();

            var tick = typeof(MqttClockRenderApp).GetMethod("ClockTick", BindingFlags.NonPublic | BindingFlags.Instance)!;
            tick.Invoke(sut, new object?[] { null, new ClockTickEventArgs(new DateTime(2025, 1, 1, 8, 5, 1)) });

            _mockAwtrixService.Verify(x => x.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()), Times.Never);
        }
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps.MqttRender"`
Expected: 5 FAIL:
- `MessageDispatchedAfterDeactivation_IsIgnored`: AppUpdate called once.
- `SubscribeThatNeverCompletes_DoesNotOutliveTheWindow`: `TimeoutException`.
- `AfterActiveTimeEnds_ClockTicksNoLongerPublish`: AppUpdate called.
- `RepeatedActivations_AttachOnlyOneClockTickHandler`: expected once, was 4 times.
- `ClockTickDeliveredAfterDeactivation_IsIgnored`: AppUpdate called.

`MessageAfterActiveTimeEnds_IsNotRendered` already passes (Task 2 detaches the handler) and stays as a regression guard. The existing tests, including WS2's retained-message tests, pass.

- [ ] **Step 3: Implement `MqttRenderApp` (replace the whole file)**

```csharp
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using MQTTnet;
using System.Text;

namespace AwtrixSharpWeb.Apps.MqttRender
{
    /// <summary>
    /// Render a subscribed MQTT payload while the scheduled window is active
    /// </summary>
    public class MqttRenderApp : ScheduledApp<MqttAppConfig>
    {
        IMqttConnector _mqttConnector;

        public MqttRenderApp(
         ILogger logger
         , IClock clock
         , MqttAppConfig config
         , AwtrixAddress awtrixAddress
         , IAwtrixService awtrixService
         , IMqttConnector mqttConnector) : base(logger, clock, awtrixAddress, awtrixService, config)
        {
            _mqttConnector = mqttConnector;
        }

        protected override async Task OnActivateAsync(ScheduledActivation activation)
        {
            // Attach first: a retained message can arrive before Subscribe returns (CR-33)
            _mqttConnector.MessageReceived += RawMessageReceived;

            // A SUBACK that never arrives must not keep the window open past ActiveTime
            await _mqttConnector.Subscribe(Config.ReadTopic).WaitAsync(activation.Token);
        }

        protected override Task OnDeactivateAsync(ScheduledActivation activation)
        {
            _mqttConnector.MessageReceived -= RawMessageReceived;
            return Task.CompletedTask;
        }

        private Task RawMessageReceived(MqttApplicationMessageReceivedEventArgs arg)
        {
            if (CurrentActivation is not { IsEnded: false })
            {
                return Task.CompletedTask; // a dispatch already in flight when the window ended
            }

            // the client can be subscribed to multiple topics, so we need to filter here
            if (arg.ApplicationMessage.Topic == Config.ReadTopic)
            {
                return HandleMessage(arg);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// If invoked, then this is the correct topic
        /// </summary>
        protected virtual Task HandleMessage(MqttApplicationMessageReceivedEventArgs arg)
        {
            string textPayload = Encoding.UTF8.GetString(arg.ApplicationMessage.Payload);

            var message = new AwtrixAppMessage()
                            .SetText(textPayload);

            var valueMap = Config.FindMatchingValueMap(textPayload);

            if (valueMap != null)
            {
                Logger.LogDebug("Found matching value map for status: {StatusText}", textPayload);

                valueMap.Decorate(message, Logger);

                // If no text is set in the mapping, use the original status text
                if (message.Text == null)
                {
                    message.SetText(textPayload);
                }
            }

            return AppUpdate(message);
        }
    }
}
```

- [ ] **Step 4: Implement `MqttClockRenderApp` (replace the whole file)**

```csharp
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using MQTTnet;
using System.Text;

namespace AwtrixSharpWeb.Apps.MqttRender
{
    /// <summary>
    /// Render the time and an MQTT message
    /// </summary>
    public class MqttClockRenderApp : MqttRenderApp
    {
        ITimerService _timerService;
        string _mqttValue = "";
        DateTime _currentTime = DateTime.MinValue;

        public MqttClockRenderApp(
             ILogger logger
             , IClock clock
             , MqttAppConfig config
             , AwtrixAddress awtrixAddress
             , IAwtrixService awtrixService
             , IMqttConnector mqttConnector
            , ITimerService timerService) : base(logger, clock, config, awtrixAddress, awtrixService, mqttConnector)
        {
            _timerService = timerService;
        }

        protected override async Task OnActivateAsync(ScheduledActivation activation)
        {
            _timerService.SecondChanged += ClockTick;

            await base.OnActivateAsync(activation);
        }

        protected override async Task OnDeactivateAsync(ScheduledActivation activation)
        {
            // CR-09: without this every activation added a handler that kept publishing after the window ended
            _timerService.SecondChanged -= ClockTick;

            await base.OnDeactivateAsync(activation);
        }

        private void ClockTick(object? sender, ClockTickEventArgs e)
        {
            if (CurrentActivation is not { IsEnded: false })
            {
                return; // a tick already dispatched when the window ended
            }

            _currentTime = e.Time;
            _ = FireAndLog(() => UpdateDisplay(), nameof(ClockTick));
        }

        protected override async Task HandleMessage(MqttApplicationMessageReceivedEventArgs arg)
        {
            _mqttValue = Encoding.UTF8.GetString(arg.ApplicationMessage.Payload);
            await UpdateDisplay();
        }

        private Task<bool> UpdateDisplay()
        {
            var clockText = TimerService.FormatClockString(_currentTime, true);

            var messageText = $"{clockText} {_mqttValue}";

            var message = new AwtrixAppMessage()
                                .SetText(messageText)
                                .SetDuration(3600);

            return AppUpdate(message);
        }
    }
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps.MqttRender"`
Expected: PASS, 0 failed.

- [ ] **Step 6: Run the full suite and code checks**

Run: `dotnet test`
Expected: 0 failed.

`grep -n "DateTimeOffset.Now" test/Test/Apps/MqttRender/*.cs` → no matches.

- [ ] **Step 7: Commit**

```bash
git add src/api/Apps/MqttRender/MqttRenderApp.cs src/api/Apps/MqttRender/MqttClockRenderApp.cs test/Test/Apps/MqttRender/MqttRenderAppTests.cs test/Test/Apps/MqttRender/MqttClockRenderAppTests.cs
git commit -m "fix(mqtt-render): stop publishing when the window ends (CR-09)

MqttClockRenderApp detaches SecondChanged on deactivation, so repeated daily
activations no longer stack handlers that republish after the slot is cleared.
Late ticks and message dispatches are ignored once the activation has ended,
and a SUBACK that never arrives no longer keeps a window open past ActiveTime.
Tests use a pinned FakeTimeProvider.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 4: TripTimer completes through its activation and ignores late ticks (CR-19, WS1 deferred a/b)

**Files:**
- Modify: `src/api/Apps/TripTimer/TripTimerApp.cs` (full replace)
- Modify: `test/Test/Apps/TripTimer/TripTimerAppTickTests.cs` (full replace)
- Modify: `test/Test/Apps/TripTimer/TripTimerAppBoundaryTests.cs` (departures seeding and `BuildMessage` calls)
- Modify: `test/Test/Apps/TripTimerAppTests.cs` (pinned clock)

**Interfaces:**
- Consumes (Task 2): `OnActivateAsync`/`OnDeactivateAsync`, `CurrentActivation`, `ScheduledActivation.Complete()`/`IsEnded`/`Number`/`Trigger`/`Token`, `LastRun`, `IClock.TimeProvider`.
- Produces (WS6 relies on these):
  - `internal IReadOnlyList<TripSummary> TripTimerApp.NextDepartures` (read-only snapshot)
  - `internal void TripTimerApp.SetDepartures(IEnumerable<TripSummary> departures)` (rounds to the minute and swaps atomically)
  - `internal AwtrixAppMessage? TripTimerApp.BuildMessage(DateTime tickTime)` (null when no future alarm)
- Unchanged: `GetAlarmTime`, `GetProgress`, `AlarmStages`, the constructor.

- [ ] **Step 1: Write the failing tests**

Replace the whole of `test/Test/Apps/TripTimer/TripTimerAppTickTests.cs`:

```csharp
using System.Reflection;
using AwtrixSharpWeb.Apps.TripTimer;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Test.Domain;

namespace Test.Apps.TripTimer
{
    /// <summary>
    /// TripTimerApp activation lifecycle on a pinned FakeTimeProvider: countdown ticks, the no-departures completion
    /// path (CR-19), and ticks that arrive after the window ended or was superseded (WS1 deferred a/b).
    /// </summary>
    public class TripTimerAppTickTests
    {
        private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

        // FakeTimeProvider's local zone is UTC, so every instant here is UTC
        private static readonly DateTimeOffset Now = new(2025, 8, 19, 6, 30, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset Departure = new(2025, 8, 19, 6, 41, 0, TimeSpan.Zero);

        private readonly FakeTimeProvider _time = new(Now);
        private readonly Mock<ILogger> _logger = new();
        private readonly Mock<IAwtrixService> _awtrix = new();
        private readonly Mock<ITimerService> _timer = new();
        private readonly Mock<ITripPlannerService> _planner = new();
        private readonly TaskCompletionSource<bool> _slowClear = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _clearIsSlow;

        public TripTimerAppTickTests()
        {
            _awtrix.Setup(a => a.Notify(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixAppMessage>())).ReturnsAsync(true);
            _awtrix.Setup(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>())).ReturnsAsync(true);
            _awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>()))
                .Returns(() => _clearIsSlow ? _slowClear.Task : Task.FromResult(true));
            _planner.Setup(p => p.GetNextDepartures(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new List<TripSummary> { TripSummaryTests.Create(Departure) });
        }

        private TripTimerApp CreateApp()
        {
            var config = new TripTimerAppConfig
            {
                CronSchedule = "10 6 * * 1-5",
                ActiveTime = TimeSpan.FromMinutes(30),
                TimeToOrigin = TimeSpan.Zero,
                TimeToPrepare = TimeSpan.Zero,
            };
            config.Type = AppNames.TripTimerApp;

            return new TripTimerApp(_logger.Object, new Clock(_time), new AwtrixAddress { BaseTopic = "awtrix/clock1" },
                _awtrix.Object, _timer.Object, config, _planner.Object);
        }

        private void RaiseSecond() =>
            _timer.Raise(t => t.SecondChanged += null, this, new ClockTickEventArgs(_time.GetLocalNow().DateTime));

        private void InvokeClockTickSecond(TripTimerApp app)
        {
            var method = typeof(TripTimerApp).GetMethod("ClockTickSecond", BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(app, new object?[] { null, new ClockTickEventArgs(_time.GetLocalNow().DateTime) });
        }

        private int SecondChangedAdds() => _timer.Invocations.Count(i => i.Method.Name == "add_SecondChanged");

        private void VerifyNoErrorsLogged() =>
            _logger.Verify(x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);

        [Fact]
        public async Task Tick_WhileActive_PublishesCountdownToNextAlarm()
        {
            var app = CreateApp();
            app.ExecuteNow();

            RaiseSecond();

            _awtrix.Verify(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), AppNames.TripTimerApp,
                It.Is<AwtrixAppMessage>(m => m.Text != null && m.Text.Contains("->41"))), Times.Once);
            await app.DisposeAsync();
        }

        [Fact]
        public async Task NoFutureDepartures_CompletesActivation_WithoutPublishingAnEmptyPayload()
        {
            // CR-19: used to publish {} (a blank page, not a delete) and cancel a shared field
            var app = CreateApp();
            app.ExecuteNow();
            _time.Advance(Departure - Now + TimeSpan.FromMinutes(1)); // 06:42: the only alarm has passed; still inside ActiveTime

            RaiseSecond();

            await app.LastRun.WaitAsync(Guard);
            _awtrix.Verify(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()), Times.Never);
            _timer.VerifyRemove(t => t.SecondChanged -= It.IsAny<EventHandler<ClockTickEventArgs>>(), Times.Once);
            _awtrix.Verify(a => a.AppClear(It.IsAny<AwtrixAddress>(), AppNames.TripTimerApp), Times.Exactly(2)); // start + end
            VerifyNoErrorsLogged();
        }

        [Fact]
        public async Task NoFutureDepartures_TickReturnsPromptly_EvenWhenFinalAppClearIsSlow()
        {
            var app = CreateApp();
            app.ExecuteNow();
            _time.Advance(Departure - Now + TimeSpan.FromMinutes(1));
            _clearIsSlow = true;

            try
            {
                await Task.Run(RaiseSecond).WaitAsync(Guard);
                Assert.False(app.LastRun.IsCompleted); // teardown is parked on AppClear, off the tick thread
            }
            finally
            {
                _slowClear.TrySetResult(true);
            }

            await app.LastRun.WaitAsync(Guard);
        }

        [Fact]
        public async Task TickDeliveredAfterDispose_IsIgnored_AndLogsNoError()
        {
            // WS1 deferred (a): TimerService snapshots its invocation list, so a tick can arrive after unsubscribe
            var app = CreateApp();
            app.ExecuteNow();
            await app.DisposeAsync();
            _awtrix.Invocations.Clear();

            InvokeClockTickSecond(app);

            _awtrix.Verify(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()), Times.Never);
            VerifyNoErrorsLogged();
        }

        [Fact]
        public async Task ExecuteNowDuringActiveRun_OldTeardownDoesNotRemoveTheNewTickHandler()
        {
            // WS1 deferred (b): the old activation's late "-=" used to remove the new activation's handler
            var app = CreateApp();
            app.ExecuteNow();
            _clearIsSlow = true;

            app.ExecuteNow(); // supersedes #1, which unsubscribes and then parks on its final AppClear
            Assert.Equal(1, SecondChangedAdds()); // #2 waits for #1's teardown before wiring up

            _clearIsSlow = false;
            _slowClear.TrySetResult(true);
            Assert.True(SpinWait.SpinUntil(() => SecondChangedAdds() == 2, Guard));

            _timer.VerifyRemove(t => t.SecondChanged -= It.IsAny<EventHandler<ClockTickEventArgs>>(), Times.Once);
            _awtrix.Invocations.Clear();
            RaiseSecond();
            _awtrix.Verify(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), AppNames.TripTimerApp, It.IsAny<AwtrixAppMessage>()), Times.Once);
            await app.DisposeAsync();
        }

        [Fact]
        public async Task SetDepartures_ReplacesTheList_AndRoundsToTheMinute()
        {
            var app = CreateApp();

            app.SetDepartures(new[] { TripSummaryTests.Create(Departure.AddSeconds(20)) });

            var only = Assert.Single(app.NextDepartures);
            Assert.Equal(0, only.Origin.Time.Second);
            await app.DisposeAsync();
        }
    }
}
```

In `test/Test/Apps/TripTimer/TripTimerAppBoundaryTests.cs`:
1. In `GetSystemUnderTest`, replace:

```csharp
            sut.NextDepartures.Clear();
            sut.NextDepartures.Add(TripSummaryTests.Create(departureTime));
```

with:

```csharp
            sut.SetDepartures(new[] { TripSummaryTests.Create(departureTime) });
```

2. Replace the whole `InvokeBuildMessage` method with:

```csharp
        private static AwtrixAppMessage InvokeBuildMessage(TripTimerApp sut, DateTime tickTime)
        {
            var message = sut.BuildMessage(tickTime);
            Assert.NotNull(message);
            return message!;
        }
```

3. Replace the whole test `BuildMessage_NoFutureDepartures_ReturnsEmptyMessage` with:

```csharp
        [Fact]
        public void BuildMessage_NoFutureDepartures_ReturnsNull()
        {
            // CR-19: null means "nothing to show"; the tick handler completes the activation instead of publishing {}
            var departureTime = DateTimeOffset.Parse("2025-08-19T06:41:00+10:00");
            var now = departureTime.AddMinutes(5);
            var sut = GetSystemUnderTest(now, departureTime);

            Assert.Null(sut.BuildMessage(now.DateTime));
        }
```

In `test/Test/Apps/TripTimerAppTests.cs`, replace `_clock = new MockClock(DateTimeOffset.Now);` with `_clock = new MockClock(DateTimeOffset.Parse("2025-08-19T05:30:00+10:00"));`.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps.TripTimer|FullyQualifiedName~Test.Apps.TripTimerAppTests"`
Expected: build FAILS with `CS1061: 'TripTimerApp' does not contain a definition for 'SetDepartures'` and `CS1503` on `BuildMessage(DateTime)`.

- [ ] **Step 3: Implement (replace the whole of `src/api/Apps/TripTimer/TripTimerApp.cs`)**

```csharp
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Services.TripPlanner;

namespace AwtrixSharpWeb.Apps.TripTimer
{
    /// <summary>
    /// Calculate the best times to get ready and leave for the train station
    /// </summary>
    public class TripTimerApp : ScheduledApp<TripTimerAppConfig>
    {
        private readonly ITripPlannerService _tripPlanner;
        private readonly ITimerService _timerService;
        private volatile IReadOnlyList<TripSummary> _nextDepartures = Array.Empty<TripSummary>();

        /// <summary>
        /// How long before the alarm actually triggers do we show the visual alert
        /// </summary>
        private readonly TimeSpan VisualAlertBuffer;

        internal class AlarmStages
        {
            /// <summary>
            /// When you have to start getting ready to leave
            /// </summary>
            public DateTimeOffset PrepareForDepartTime { get; set; }

            /// <summary>
            /// When you have to leave for the origin station
            /// </summary>
            public DateTimeOffset DepartForOriginTime { get; set; }

            /// <summary>
            /// When the train departs from the origin station
            /// </summary>
            public DateTimeOffset OriginDepartTime { get; set; }

            public override string ToString()
            {
                return $"{PrepareForDepartTime:HH:mm} -> {DepartForOriginTime:HH:mm} -> {OriginDepartTime:HH:mm}";
            }
        }

        public TripTimerApp(
            ILogger logger
            , IClock clock
            , AwtrixAddress awtrixAddress
            , IAwtrixService awtrixService
            , ITimerService timerService
            , TripTimerAppConfig config
            , ITripPlannerService tripPlanner) : base(logger, clock, awtrixAddress, awtrixService, config)
        {
            _tripPlanner = tripPlanner;
            _timerService = timerService;

            VisualAlertBuffer = TimeSpan.FromSeconds(20);
        }

        /// <summary>
        /// The departures the countdown is built from, rounded to the minute. A snapshot; replaced atomically.
        /// </summary>
        internal IReadOnlyList<TripSummary> NextDepartures => _nextDepartures;

        /// <summary>
        /// Replace the departures the countdown uses. Safe to call from any thread while active
        /// (WS6's periodic refresh calls this with the activation's token).
        /// </summary>
        internal void SetDepartures(IEnumerable<TripSummary> departures)
        {
            // Round to the minute otherwise we get to alarm time and it isn't aligned to minute boundaries
            var rounded = departures.Select(d => d.AsRounded()).ToList();
            _nextDepartures = rounded;

            Logger.LogInformation("{Count} future departures computed (Prep -> Leave -> Departure):", rounded.Count);
            foreach (var departure in rounded)
            {
                Logger.LogInformation("{AlarmStages}", GetAlarmTime(departure));
            }
        }

        protected override async Task OnActivateAsync(ScheduledActivation activation)
        {
            Logger.LogInformation("Trip timer activated ({Trigger})", activation.Trigger);

            var message = new AwtrixAppMessage()
                .SetText("Starting trip timer")
                .SetStack(false);

            await Notify(message);

            // Find the earliest we could get to the train station and query from then
            var earliestDeparture = Clock.Now.Add(Config.TimeToOrigin).Add(Config.TimeToPrepare);

            var departures = await _tripPlanner
                .GetNextDepartures(Config.StopIdOrigin, Config.StopIdDestination, earliestDeparture.LocalDateTime)
                .WaitAsync(activation.Token);

            SetDepartures(departures);

            _timerService.SecondChanged += ClockTickSecond;
        }

        protected override Task OnDeactivateAsync(ScheduledActivation activation)
        {
            _timerService.SecondChanged -= ClockTickSecond;
            Logger.LogInformation("Trip timer activation #{Number} deactivated", activation.Number);
            return Task.CompletedTask;
        }

        private void ClockTickSecond(object? sender, ClockTickEventArgs e)
        {
            var activation = CurrentActivation;
            if (activation is not { IsEnded: false })
            {
                return; // a tick dispatched after the window ended, was superseded, or the app was disposed
            }

            // Runs inside FireAndLog so nothing can escape onto the timer loop
            _ = FireAndLog(async () =>
            {
                var message = BuildMessage(e.Time);
                if (message == null)
                {
                    // CR-19: nothing left to show. End this activation; deactivation (unsubscribe + AppClear)
                    // runs on the thread pool, never on the tick thread.
                    Logger.LogInformation("No future departures; ending trip timer activation #{Number}", activation.Number);
                    activation.Complete();
                    return;
                }

                if (!activation.IsEnded)
                {
                    await AppUpdate(message);
                }
            }, nameof(ClockTickSecond));
        }

        /// <summary>
        /// The countdown frame for <paramref name="tickTime"/>, or null when no alarm is in the future.
        /// </summary>
        internal AwtrixAppMessage? BuildMessage(DateTime tickTime)
        {
            var alarmTimes = NextDepartures.Select(GetAlarmTime)
                .Where(alarmTime => alarmTime.PrepareForDepartTime > Clock.Now)
                .Select(at => at.PrepareForDepartTime)
                .Order()
                .ToList();

            if (alarmTimes.Count == 0)
            {
                return null;
            }

            var nextAlarm = alarmTimes.First();
            var timeToAlarm = nextAlarm - Clock.Now;

            var clockText = TimerService.FormatClockString(tickTime, false);

            var nowColor = "00FF00";

            if (nextAlarm.AddMinutes(-1) <= Clock.Now)
            {
                // We are in the last minute before the alarm
                nowColor = "FFA500";
            }

            var jsonFormat = @"[
	{
	  ""t"": ""(NOW_TIME)"",
	  ""c"": ""(NOW_COLOR)""
	},
	{
	  ""t"": "" ->(ALARM_TIME)"",
	  ""c"": ""FF0000""
	}
]";

            var text = jsonFormat
                .Replace("(NOW_TIME)", clockText)
                .Replace("(NOW_COLOR)", nowColor)
                .Replace("(ALARM_TIME)", $"{nextAlarm:mm}");

            var quantisedProgress = GetProgress(Clock, nextAlarm);
            var useProgress = quantisedProgress.quantized;

            if (clockText.Contains(":"))    // Is an odd second
            {
                useProgress = quantisedProgress.quantizedBlink;
            }

            var message = new AwtrixAppMessage()
                .SetText(text)
                .SetStack(false)
                .SetDuration(300)
                .SetProgress(useProgress);

            if (timeToAlarm < VisualAlertBuffer)
            {
                if (Config.ValueMaps.Any())
                {
                    Config
                        .ValueMaps[0]
                        .Decorate(message, Logger);
                }
                else
                {
                    message
                        .SetText("GO!")
                        .SetRainbow()
                        .SetProgress(100);

                    Logger.LogInformation("{Text}", message.Text);
                }
            }

            return message;
        }

        internal (int quantized, int quantizedBlink) GetProgress(IClock clock, DateTimeOffset nextAlarm)
        {
            const int ZeroFromMinutes = 5;

            var countFromSecs = (int)(TimeSpan.FromMinutes(ZeroFromMinutes) - VisualAlertBuffer).TotalSeconds; // ensure full progress bar
            var secondsSinceCountFrom = (int)(clock.Now - nextAlarm.AddMinutes(-ZeroFromMinutes)).TotalSeconds;
            var progress = secondsSinceCountFrom * 100 / countFromSecs;

            var quantizedProgress = AwtrixService.Quantize(progress);
            return quantizedProgress;
        }

        internal AlarmStages GetAlarmTime(TripSummary originDepartTime)
        {
            var departForOriginTime = originDepartTime.Origin.Time.Add(-Config.TimeToOrigin);
            var prepareForDepartTime = departForOriginTime.Add(-Config.TimeToPrepare);
            return new AlarmStages { OriginDepartTime = originDepartTime.Origin.Time, DepartForOriginTime = departForOriginTime, PrepareForDepartTime = prepareForDepartTime };
        }
    }
}
```

Note: the `MinuteChanged` debug-log handler is removed on purpose (spec D10).

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps.TripTimer|FullyQualifiedName~Test.Apps.TripTimerAppTests|FullyQualifiedName~Test.Apps.AppDisposalTests"`
Expected: PASS, 0 failed.

If `SetDepartures_ReplacesTheList_AndRoundsToTheMinute` fails because `TripSummary.AsRounded()` rounds differently (for example, to the nearest minute), keep the implementation and change the assertion to the documented `AsRounded` behaviour. The test only guards that `SetDepartures` applies it.

- [ ] **Step 5: Run the full suite and code checks**

Run: `dotnet test`
Expected: 0 failed. WS3's `StartAsync_RightDoubleClick_StartsOnlyThatDevicesTripTimerOnce` still sees exactly one `Notify` per double-click.

Run each check. The expected result follows each arrow.
- `grep -n "_cts\|MinuteChanged\|new AwtrixAppMessage();" src/api/Apps/TripTimer/TripTimerApp.cs` → no matches.
- `grep -n "DateTimeOffset.Now" test/Test/Apps/TripTimerAppTests.cs test/Test/Apps/TripTimer/*.cs test/Test/Apps/ScheduledAppTests.cs` → no matches.

- [ ] **Step 6: Commit**

```bash
git add src/api/Apps/TripTimer/TripTimerApp.cs test/Test/Apps/TripTimer/TripTimerAppTickTests.cs test/Test/Apps/TripTimer/TripTimerAppBoundaryTests.cs test/Test/Apps/TripTimerAppTests.cs
git commit -m "fix(trip-timer): end the activation instead of publishing {} (CR-19)

BuildMessage is pure and returns null when no alarm is left; the tick handler
completes its own activation, so deactivation unsubscribes and clears the slot
off the timer thread. Ticks that arrive after the window ended or the app was
disposed are ignored without logging errors. Departures are swapped
atomically through SetDepartures, ready for WS6's periodic refresh.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 5: Restore the ValueMap tests (CR-39)

**Files:**
- Modify: `test/Test/Configs/ValueMapsTests.cs` (full replace; the file is entirely commented out today)

**Interfaces:**
- Consumes: `ValueMap.IsMatch(string)`, `ValueMap.Decorate(AwtrixAppMessage, ILogger)`, `ValueMap.ValueMatcher`, `AppConfig.ValueMaps`, `AppConfig.FindMatchingValueMap(string)`. All exist; no production change.
- Produces: none.

This task is test-only. The restored tests pin today's behaviour, which WS5 keeps (WS5 spec: `IsMatch` unchanged, empty `ValueMatcher` valid, existing ValueMap tests pass). So the TDD "fail first" step is replaced by checking that each test **would** fail against a broken implementation (Step 3).

- [ ] **Step 1: Write the tests**

Replace the whole of `test/Test/Configs/ValueMapsTests.cs`:

```csharp
using System.Text.Json;
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using Microsoft.Extensions.Logging;
using Moq;

namespace Test.Configs
{
    /// <summary>
    /// Restored ValueMap tests (CR-39): matching semantics, deserialisation of the appsettings shape, first-match-wins
    /// selection, and match-then-decorate end to end. Complements ValueMapDecorateTests (per-setter coverage).
    /// Keys use PascalCase as in appsettings.json; case-insensitive keys are WS7 (CR-22).
    /// </summary>
    public class ValueMapsTests
    {
        private readonly Mock<ILogger> _mockLogger = new();

        [Fact]
        public void IsMatch_WithValidRegex_MatchesAlternatives()
        {
            var valueMap = new ValueMap { { "ValueMatcher", "busy|in.*meeting" } };

            Assert.True(valueMap.IsMatch("busy"));
            Assert.True(valueMap.IsMatch("in a meeting"));
            Assert.True(valueMap.IsMatch("in important meeting"));
            Assert.False(valueMap.IsMatch("available"));
            Assert.False(valueMap.IsMatch("free"));
        }

        [Fact]
        public void IsMatch_WithInvalidRegex_FallsBackToContains()
        {
            var valueMap = new ValueMap { { "ValueMatcher", "[this is not a valid regex" } };

            Assert.True(valueMap.IsMatch("meeting with [this is not a valid regex"));
            Assert.False(valueMap.IsMatch("available"));
        }

        [Fact]
        public void IsMatch_WithMissingOrEmptyMatcher_ReturnsFalse()
        {
            Assert.False(new ValueMap().IsMatch("any text"));
            Assert.False(new ValueMap { { "ValueMatcher", "" } }.IsMatch("any text"));
        }

        [Fact]
        public void IsMatch_IsCaseInsensitive()
        {
            var valueMap = new ValueMap { { "ValueMatcher", "busy" } };

            Assert.True(valueMap.IsMatch("Busy"));
            Assert.True(valueMap.IsMatch("BUSY"));
            Assert.True(valueMap.IsMatch("I am BUSY today"));
        }

        [Theory]
        [InlineData("-120", true, false)]
        [InlineData("350", false, true)]
        [InlineData("0", false, true)]
        public void IsMatch_ShippedMqttRenderMatchers_SplitNegativeAndNonNegative(string reading, bool negative, bool nonNegative)
        {
            // The two ValueMaps shipped for MqttRenderApp in appsettings.json
            Assert.Equal(negative, new ValueMap { { "ValueMatcher", "^-" } }.IsMatch(reading));
            Assert.Equal(nonNegative, new ValueMap { { "ValueMatcher", "^(?!-).*" } }.IsMatch(reading));
        }

        [Fact]
        public void Decorate_AppliesSpeedAndCenterProperties()
        {
            var valueMap = new ValueMap
            {
                { "ValueMatcher", "busy" },
                { "Center", "true" },
                { "EffectSpeed", "50" },
                { "ScrollSpeed", "100" }
            };
            var message = new AwtrixAppMessage();

            valueMap.Decorate(message, _mockLogger.Object);

            Assert.Equal("true", message["center"]);
            Assert.Equal("50", message["effectSpeed"]);
            Assert.Equal("100", message["scrollSpeed"]);
            Assert.False(message.ContainsKey("ValueMatcher"));
        }

        [Fact]
        public void Deserialize_AppsettingsShape_ProducesValueMaps()
        {
            const string json = @"[
                { ""ValueMatcher"": ""busy"", ""Icon"": ""12345"", ""Color"": ""255,0,0"", ""Text"": ""I am busy"" },
                { ""ValueMatcher"": ""meeting"", ""Icon"": ""54321"", ""Color"": ""0,0,255"", ""Duration"": ""60"" }
            ]";

            var valueMaps = JsonSerializer.Deserialize<List<ValueMap>>(json);

            Assert.NotNull(valueMaps);
            Assert.Equal(2, valueMaps!.Count);
            Assert.Equal("busy", valueMaps[0].ValueMatcher);
            Assert.Equal("12345", valueMaps[0]["Icon"]);
            Assert.Equal("I am busy", valueMaps[0]["Text"]);
            Assert.Equal("meeting", valueMaps[1].ValueMatcher);
            Assert.Equal("60", valueMaps[1]["Duration"]);
        }

        [Fact]
        public void FindMatchingValueMap_FirstMatchWins()
        {
            var config = new AppConfig
            {
                ValueMaps = new List<ValueMap>
                {
                    new() { { "ValueMatcher", "busy" }, { "Icon", "first" } },
                    new() { { "ValueMatcher", "bus" }, { "Icon", "second" } }
                }
            };

            Assert.Equal("first", config.FindMatchingValueMap("busy today")!["Icon"]);
            Assert.Equal("second", config.FindMatchingValueMap("bus stop")!["Icon"]);
            Assert.Null(config.FindMatchingValueMap("available"));
        }

        [Fact]
        public void DeserializeMatchAndDecorate_EndToEnd()
        {
            const string json = @"[{""ValueMatcher"":""busy"",""Icon"":""12345"",""Color"":""255,0,0"",""Text"":""Busy Status"",""Center"":""true"",""Duration"":""45""}]";
            var config = new AppConfig { ValueMaps = JsonSerializer.Deserialize<List<ValueMap>>(json)! };
            var message = new AwtrixAppMessage();

            var match = config.FindMatchingValueMap("I am busy with meetings");
            Assert.NotNull(match);
            match!.Decorate(message, _mockLogger.Object);

            Assert.Equal("Busy Status", message.Text);
            Assert.Equal("12345", message["icon"]);
            Assert.Equal("255,0,0", message["color"]);
            Assert.Equal("true", message["center"]);
            Assert.Equal("45", message["duration"]);
        }
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Configs.ValueMapsTests"`
Expected: PASS, 11 tests (8 facts + 3 theory rows), 0 failed.

If `Decorate_AppliesSpeedAndCenterProperties` fails on the `ContainsKey("ValueMatcher")` assertion or a key name, check the exact key used by `AwtrixAppMessage.SetEffectSpeed`/`SetScrollSpeed` and fix the **test's** expected key. Do not edit `ValueMap.cs` or `AwtrixAppMessage.cs`, which are WS5's.

- [ ] **Step 3: Prove the tests bite (no commit of this step)**

Temporarily change `RegexOptions.IgnoreCase` to `RegexOptions.None` in `src/api/Apps/Configs/ValueMap.cs` and re-run the Step 2 command.
Expected: `IsMatch_IsCaseInsensitive` FAILS.

Then restore the file exactly: `git checkout -- src/api/Apps/Configs/ValueMap.cs`. Confirm that `git status --short` lists only `test/Test/Configs/ValueMapsTests.cs`.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: 0 failed. The passed count is the Task 0 baseline plus the tests added in Tasks 1-5.

`grep -c "^//" test/Test/Configs/ValueMapsTests.cs` → `0`.

- [ ] **Step 5: Commit**

```bash
git add test/Test/Configs/ValueMapsTests.cs
git commit -m "test(valuemap): restore ValueMapsTests (CR-39)

The file was entirely commented out and referenced a removed converter.
Restores the matching, deserialisation, first-match-wins and end-to-end tests
against the current ValueMap/AppConfig API, including the shipped MqttRender
matchers.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

## Self-review (completed by planner)

| Spec item | Task |
|---|---|
| D1 activation object, hooks, `CurrentActivation`, `CancellationScope` | T2 |
| D2 state machine, `NextWakeUp`/`LastRun` seams | T2 |
| D3 serialised activations; synchronous prefix | T2 (engine test), T4 (TripTimer deferred b) |
| D4 ActiveTime for both triggers; missing/zero/clamp | T2 |
| D5 cron occurrences skipped while active | T2 (`ExecuteNow_WhileWaiting…` passes 08:00 during a run) |
| D6 `IClock.TimeProvider`, zone-correct cron, chunked delay, retry | T2 |
| D7 bounded, always-run deactivation; OCE → Debug | T2 |
| D8 dispose pattern, no Dismiss, event-app unsubscribe, TripTimer hiding Dispose removed | T1 (pattern), T2 (ScheduledApp hooks) |
| D9 MqttRender guard, bounded subscribe, CR-09 | T3 |
| D10 TripTimer null message, `Complete`, guard, `SetDepartures`, minute handler removed | T4 |
| D11 tests: fake time, pinned clocks, ValueMapsTests | T2, T3, T4, T5 |
| Acceptance 6.1 (CR-08) §1-8 | T2 (§8 by Step 8 greps) |
| Acceptance 6.2 (CR-18) §1-2 | T2 |
| Acceptance 6.3 (CR-09) §1-4 | T3 |
| Acceptance 6.4 (CR-19) §1-4 | T4 |
| Acceptance 6.5 (CR-31) §1-6 | T1 (§1-4, §6), T1+T2 (§5) |
| Acceptance 6.6 (CR-39) §1-3 | T5, T3/T4 greps, T2 grep |
| Deferred a / b / c / d / e | T4 / T2+T4 / T2 / T1 / T1 |

- **Placeholder scan:** none. Every code step shows complete code or an exact old → new replacement. Two steps say what to adjust **only** if an assumption about existing helpers (`AsRounded`, message key names) turns out different, and name the file to change.
- **Type consistency:**
  - `ScheduledActivation` members (`Number`, `Trigger`, `StartedAt`, `ActiveTime`, `Token`, `IsEnded`, `Complete`, internal `WasActivated`/`Ended`/`Release`) are defined in T2 and used unchanged in T3/T4.
  - `OnActivateAsync`/`OnDeactivateAsync(ScheduledActivation)`, `CurrentActivation`, `LastRun`, `NextWakeUp`, `NextDelayChunk`, `DeactivationTimeout`, `WaitRetryDelay` and `MaxDelayChunk` are defined in T2.
  - `ReleaseResources`/`DisposeCoreAsync` are defined in T1 and overridden in T2's `ScheduledApp`.
  - `SetDepartures`, `NextDepartures : IReadOnlyList<TripSummary>` and `BuildMessage(DateTime) : AwtrixAppMessage?` are defined in T4 and used in T4's tests.
- **Intermediate states compile:**
  - T1 keeps the WS1 engine with a `ReleaseResources` override.
  - T2 migrates all three subclasses mechanically: TripTimer keeps `List` `NextDepartures` and an empty-message return, and MqttClockRender keeps the CR-09 leak. T3/T4 then fix these test-first.
  - T2 rewrites `TripTimerAppTickTests` for the interim API; T4 replaces it.
- **Determinism:**
  - Tests advance `FakeTimeProvider` only after the signal that the next timer exists.
  - The chunked-wait test pumps `Advance(TimeSpan.Zero)`.
  - The retry test and the TripTimer supersede test use bounded `SpinWait.SpinUntil`, because they observe work on thread-pool continuations.
  - No `Task.Delay` or sleep anywhere in the tests.
