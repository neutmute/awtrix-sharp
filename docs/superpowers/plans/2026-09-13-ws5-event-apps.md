# WS5 Event-Driven Apps and Display Correctness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:**
- Diurnal always shows the settings that should be in effect, and bad entries are validated and skipped.
- Slack status handling is null-safe and async.
- Double-click detection uses monotonic time.
- ValueMap drives every message setter, with correct JSON arrays and invariant-culture numbers.

**Architecture:**
- **Diurnal:** a new pure `DiurnalSchedule` parses the time map once. It answers two questions: "what state is in effect at T" (a 24 h rotation) and "what is due in (last, now]". `DiurnalApp` becomes a thin adapter over it.
- **Slack:** `SlackStatusApp` moves its handler body into `FireAndLog`.
- **Buttons:** `DoubleClickDetector` takes a `TimeProvider`.
- **ValueMap:** a static `ValueMapSetters` table replaces the reflection lookup. `AwtrixAppMessage.ToJson` emits the five array keys as JSON arrays. ValueMap problems are logged once, from the `AwtrixApp` constructor.

**Tech Stack:** .NET 10, ASP.NET Core, xUnit 2.9, Moq 4.20, Microsoft.Extensions.TimeProvider.Testing 10.0.0

**Spec:** `docs/superpowers/specs/2026-09-13-ws5-event-apps-design.md`

## Global Constraints

- **Prerequisites:** WS1, WS2, WS3 and WS4 are committed. Do not start while another agent is still committing to `feature/redo`.
- **Config compatibility:** no `appsettings.json` keys or `AWTRIXSHARP_*` / `TRANSPORTOPENDATA__APIKEY` env vars are added, renamed or reinterpreted.
  - Diurnal format stays `"HHmm": "Name=Value[;Name=Value]"`, with names `Brightness` and `GlobalTextColor`, case-insensitive.
  - ValueMap keys and values keep working. An invalid `ValueMatcher` regex still falls back to a substring match. An empty `ValueMatcher` is valid and is not warned about.
- **No new configuration**, required or optional.
- **Button semantics are unchanged:** the first press is Click; a second press within 300 ms (inclusive) is DoubleClick and resets. Do not make them mutually exclusive.
- **Payloads:**
  - MQTT topics and HTTP URLs do not change.
  - `AwtrixSettings` JSON does not change.
  - `AwtrixAppMessage` JSON changes only for `line`, `bar`, `progressC`, `progressBC` and `gradient`, which become arrays.
  - Dictionary values such as `message["line"] == "1,2,3"` do not change.
- **No new packages.** Target `net10.0`.
- **Do not edit:**
  - `src/api/HostedServices/SlackConnector.cs`
  - `src/api/Apps/Buttons/ButtonApp.cs`
  - `src/api/HostedServices/Conductor.cs`
  - `src/api/Program.cs`
  - `src/api/Apps/ScheduledApp.cs`
  - `src/api/HostedServices/TimerService.cs`
  - `test/Test/Configs/ValueMapsTests.cs`
- **Never run the app**, a broker, a device or Slack. Unit tests only.
- **TDD per task:**
  1. Write the failing test.
  2. Run it filtered; it fails (compile errors count).
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
| `src/api/Apps/Buttons/DoubleClickDetector.cs` | monotonic `TimeProvider` timestamps | 1 |
| `src/api/Apps/Buttons/ButtonState.cs` | optional `TimeProvider` passed through | 1 |
| `test/Test/Apps/Buttons/DoubleClickDetectorTests.cs`, `ButtonStateTests.cs` | added tests (existing tests kept) | 1 |
| `src/api/Apps/Diurnal/DiurnalSchedule.cs` (new) | parse/validate; `StateAt`; `DueBetween` | 2 |
| `test/Test/Apps/Diurnal/DiurnalScheduleTests.cs` (new) | pure schedule tests | 2 |
| `src/api/Apps/Diurnal/DiurnalApp.cs` | startup rotation + (last, now] ticks | 3 |
| `test/Test/Apps/Diurnal/DiurnalAppTests.cs` | rewritten with fixed dates | 3 |
| `src/api/Apps/SlackStatus/SlackStatusApp.cs` | null-safe, async handler | 4 |
| `test/Test/Apps/SlackStatus/SlackStatusAppTests.cs` | rewritten on mocks | 4 |
| `src/api/Domain/AwtrixAppMessage.cs` | invariant culture; JSON arrays; shared parsers | 5 |
| `test/Test/Domain/AwtrixAppMessageJsonTests.cs` (new) | JSON and culture tests | 5 |
| `src/api/Apps/Configs/ValueMapSetters.cs` (new) | static setter table | 6 |
| `src/api/Apps/Configs/ValueMap.cs` | table-driven `Decorate`; `GetConfigurationProblems` | 6 |
| `src/api/Apps/Configs/AppConfig.cs` | `LogValueMapProblems` | 6 |
| `src/api/Apps/AwtrixApp.cs` | constructor calls `LogValueMapProblems` | 6 |
| `test/Test/Configs/ValueMapSetterTests.cs` (new) | table, validation and load-time warning tests | 6 |

---

### Task 0: Verify WS1-WS4 interfaces as assumed

**Files:** none modified.

- [ ] **Step 1: Check the working tree and history**

Run: `git status --short` and `git log --oneline -15`

Expected: the tree is clean, and commits for WS1 (`refactor(di): interface seams…`), WS2 (MQTT connector), WS3 (Conductor lifecycle) and WS4 (ScheduledApp) are present. If the tree is dirty, stop: another agent is still working.

- [ ] **Step 2: Check the assumed shapes**

Run each command. Expected result is shown after the arrow.
- `grep -n "InitAsync\|void Init\b\|Initialize" src/api/Interfaces/IAwtrixApp.cs src/api/Apps/AwtrixApp.cs` → `Task InitAsync()` on `IAwtrixApp` and `AwtrixApp`, plus an abstract `Initialize` hook. **Record its exact signature** (`protected abstract void Initialize()` or `protected abstract Task InitializeAsync()`).
- `grep -n "protected Task FireAndLog" src/api/Apps/AwtrixApp.cs` → `protected Task FireAndLog(Func<Task> work, string operation)`
- `grep -n "public AwtrixApp(" src/api/Apps/AwtrixApp.cs` → `public AwtrixApp(ILogger logger, TConfig config, AwtrixAddress awtrixAddress, IAwtrixService awtrixService)`
- `grep -n "Task<bool> Set\|Task<bool> AppUpdate\|Task<bool> AppClear" src/api/Apps/AwtrixApp.cs` → all three protected helpers.
- `grep -n "public DiurnalApp(" -A7 src/api/Apps/Diurnal/DiurnalApp.cs` → `(ILogger logger, IClock clock, ITimerService timerService, AppConfig config, AwtrixAddress awtrixAddress, IAwtrixService awtrixService)`
- `grep -n "public SlackStatusApp(" -A5 src/api/Apps/SlackStatus/SlackStatusApp.cs` → `(ILogger logger, SlackStatusAppConfig config, AwtrixAddress awtrixAddress, IAwtrixService awtrixService, ISlackConnector slackConnector)`
- `grep -n "UserStatusChanged" src/api/Interfaces/ISlackConnector.cs` → `event EventHandler<SlackUserStatusChangedEventArgs>? UserStatusChanged;`
- `grep -n "MinuteChanged" src/api/Interfaces/ITimerService.cs` → `event EventHandler<ClockTickEventArgs>? MinuteChanged;`
- `grep -n "GetLocalNow" src/api/Domain/Clock.cs` → `public DateTimeOffset Now => _timeProvider.GetLocalNow();`
- `grep -n "Task<bool> Set\|Task<bool> AppUpdate\|Task<bool> AppClear" src/api/Services/IAwtrixService.cs` → `Set(AwtrixAddress, AwtrixSettings)`, `AppUpdate(AwtrixAddress, string, AwtrixAppMessage)`, `AppClear(AwtrixAddress, string)`
- `grep -n "string.Join" src/api/Domain/AwtrixSettings.cs` → the safe `ToString` (WS1 D10).
- `grep -n "TimeProvider.Testing\|Moq" test/Test/Test.csproj` → both present.
- `grep -rn "DiurnalApp\|SlackStatusApp\|DoubleClickDetector\|ValueMap" src/api/Apps/ScheduledApp.cs` → no matches (WS4 did not couple to WS5 types).

- [ ] **Step 3: Adaptation rules if the shapes differ** (adapt names only; keep the behaviour in this plan)
- **Hook is `protected abstract Task InitializeAsync()`:** in Tasks 3 and 4, write `protected override Task InitializeAsync()` with the same body, ending with `return Task.CompletedTask;`. Do **not** await the startup `Set`; keep `FireAndLog` (spec D4).
- **`IAwtrixApp` still exposes `void Init()`:** in every test in this plan, replace `await app.InitAsync();` with `app.Init();`, and `await Record.ExceptionAsync(() => app.InitAsync())` with `Record.Exception(() => app.Init())`. Test methods may stay `async Task`.
- **`FireAndLog` has a different name or signature:** use the WS1/WS3 equivalent that never throws and logs.
- **`AwtrixApp` constructor gained parameters:** in Task 6, add the `LogValueMapProblems` call at the end of whatever constructor exists.
- **Record** any adaptation in the Task 1 commit message body (`Adapted: …`).

- [ ] **Step 4: Baseline**

Run: `dotnet test`

Expected: 0 failed. Note the passed and skipped counts.

Run: `git rev-parse HEAD`

Record the SHA as `WS5_BASE`. Final verification diffs against it.

---

### Task 1: DoubleClickDetector on monotonic TimeProvider timestamps (CR-32)

**Files:**
- Modify: `src/api/Apps/Buttons/DoubleClickDetector.cs` (whole file)
- Modify: `src/api/Apps/Buttons/ButtonState.cs` (whole file)
- Test: `test/Test/Apps/Buttons/DoubleClickDetectorTests.cs` (append tests; keep the existing four)
- Test: `test/Test/Apps/Buttons/ButtonStateTests.cs` (append one test; keep the existing ones)

**Interfaces:**
- Consumes: `System.TimeProvider`, `Microsoft.Extensions.Time.Testing.FakeTimeProvider`
- Produces:
  - `public DoubleClickDetector(double thresholdMilliseconds = 300, TimeProvider? timeProvider = null)`
  - `public ButtonState(Button button, string topic, TimeProvider? timeProvider = null)`

- [ ] **Step 1: Write the failing tests**

Add `using Microsoft.Extensions.Time.Testing;` at the top of `test/Test/Apps/Buttons/DoubleClickDetectorTests.cs`. Insert these members inside `public class DoubleClickDetectorTests`, after the last existing test:

```csharp
        [Fact]
        public void RegisterClick_SecondClickExactlyAtThreshold_IsDoubleClick()
        {
            var time = new FakeTimeProvider();
            var sut = new DoubleClickDetector(300, time);

            Assert.False(sut.RegisterClick());
            time.Advance(TimeSpan.FromMilliseconds(300));

            Assert.True(sut.RegisterClick());
        }

        [Fact]
        public void RegisterClick_SecondClickJustAfterThreshold_IsNotDoubleClick()
        {
            var time = new FakeTimeProvider();
            var sut = new DoubleClickDetector(300, time);

            Assert.False(sut.RegisterClick());
            time.Advance(TimeSpan.FromMilliseconds(301));

            Assert.False(sut.RegisterClick());
        }

        [Fact]
        public void RegisterClick_WallClockJumpsBackwards_IsNotDoubleClick()
        {
            // DST end / NTP step: wall clock goes back an hour while real (monotonic) time moves 5 s.
            // With DateTime.Now, "now - last" was negative and therefore <= threshold => false double-click.
            var time = new SteppableTimeProvider();
            var sut = new DoubleClickDetector(300, time);

            Assert.False(sut.RegisterClick());
            time.Step(monotonic: TimeSpan.FromSeconds(5), wallClock: TimeSpan.FromHours(-1));

            Assert.False(sut.RegisterClick());
        }

        [Fact]
        public void RegisterClick_SlowThenQuick_SecondPairIsDoubleClick()
        {
            var time = new FakeTimeProvider();
            var sut = new DoubleClickDetector(300, time);

            Assert.False(sut.RegisterClick());
            time.Advance(TimeSpan.FromSeconds(2));
            Assert.False(sut.RegisterClick());
            time.Advance(TimeSpan.FromMilliseconds(100));

            Assert.True(sut.RegisterClick());
        }

        /// <summary>
        /// Wall clock and monotonic timestamp move independently.
        /// </summary>
        private sealed class SteppableTimeProvider : TimeProvider
        {
            private long _timestamp;
            private DateTimeOffset _utcNow = new(2026, 4, 5, 16, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow() => _utcNow;

            public override long GetTimestamp() => _timestamp;

            public override long TimestampFrequency => TimeSpan.TicksPerSecond;

            public void Step(TimeSpan monotonic, TimeSpan wallClock)
            {
                _timestamp += monotonic.Ticks;
                _utcNow += wallClock;
            }
        }
```

Add `using Microsoft.Extensions.Time.Testing;` at the top of `test/Test/Apps/Buttons/ButtonStateTests.cs`. Insert inside `public class ButtonStateTests`, after the last existing test:

```csharp
        [Fact]
        public void RegisterChange_PressReleasePressSlowly_FiresTwoClicksAndNoDoubleClick()
        {
            var time = new FakeTimeProvider();
            var sut = new ButtonState(Button.Select, "topic/select", time);
            var clickCount = 0;
            var doubleClickCount = 0;
            sut.Click += (s, e) => clickCount++;
            sut.DoubleClick += (s, e) => doubleClickCount++;

            sut.RegisterChange(true);
            sut.RegisterChange(false);
            time.Advance(TimeSpan.FromMilliseconds(500));
            sut.RegisterChange(true);

            Assert.Equal(2, clickCount);
            Assert.Equal(0, doubleClickCount);
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps.Buttons"`

Expected: build FAILS with CS1739/CS1729. The constructors do not accept a `TimeProvider` yet.

- [ ] **Step 3: Implement**

Replace the whole of `src/api/Apps/Buttons/DoubleClickDetector.cs`:

```csharp
namespace AwtrixSharpWeb.Apps.MqttRender
{
    /// <summary>
    /// Detects a second click within a threshold. Uses monotonic <see cref="TimeProvider"/> timestamps,
    /// so wall-clock changes (DST end, NTP steps) can never produce a false double-click.
    /// </summary>
    public class DoubleClickDetector
    {
        private readonly TimeSpan _threshold;
        private readonly TimeProvider _timeProvider;
        private long? _lastClickTimestamp;

        public DoubleClickDetector(double thresholdMilliseconds = 300, TimeProvider? timeProvider = null)
        {
            _threshold = TimeSpan.FromMilliseconds(thresholdMilliseconds);
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <returns>true if this click completes a double-click (the detector then resets)</returns>
        public bool RegisterClick()
        {
            var now = _timeProvider.GetTimestamp();

            if (_lastClickTimestamp is long last && _timeProvider.GetElapsedTime(last, now) <= _threshold)
            {
                _lastClickTimestamp = null;
                return true;
            }

            _lastClickTimestamp = now;
            return false;
        }
    }
}
```

Replace the whole of `src/api/Apps/Buttons/ButtonState.cs`:

```csharp
namespace AwtrixSharpWeb.Apps.MqttRender
{
    public class ButtonState
    {
        private readonly DoubleClickDetector _doubleClickDetector;

        public Button Button { get; private set; } = Button.Unknown;

        public bool IsPressed { get; private set; } = false;

        public string Topic { get; private set; } = string.Empty;


        public event EventHandler<ButtonEventArgs>? Click;

        public event EventHandler<ButtonEventArgs>? DoubleClick;

        public ButtonState(Button button, string topic, TimeProvider? timeProvider = null)
        {
            Button = button;
            Topic = topic;
            _doubleClickDetector = new DoubleClickDetector(timeProvider: timeProvider);
        }

        public void RegisterChange(bool newIsPressed)
        {
            if (!IsPressed && newIsPressed)
            {
                var isDoubleClick = _doubleClickDetector.RegisterClick();
                if (isDoubleClick)
                {
                    DoubleClick?.Invoke(this, new ButtonEventArgs { Button = Button });
                }
                else
                {
                    Click?.Invoke(this, new ButtonEventArgs { Button = Button });
                }
            }
            IsPressed = newIsPressed;
        }

        public override string ToString()
        {
            return $"Button={Button}, IsPressed={IsPressed}";
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps.Buttons"`

Expected: PASS. This includes all pre-existing detector, ButtonState and ButtonApp tests.

Then run: `dotnet test`

Expected: 0 failed.

Then run: `grep -rn "DateTime.Now" src/api/Apps/Buttons`

Expected: no output.

- [ ] **Step 5: Commit**

```bash
git add src/api/Apps/Buttons/DoubleClickDetector.cs src/api/Apps/Buttons/ButtonState.cs test/Test/Apps/Buttons/DoubleClickDetectorTests.cs test/Test/Apps/Buttons/ButtonStateTests.cs
git commit -m "fix(buttons): monotonic TimeProvider timestamps for double-click detection (CR-32)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---
### Task 2: DiurnalSchedule — eager validation, rotation state, due window (CR-21, CR-20 core)

**Files:**
- Create: `src/api/Apps/Diurnal/DiurnalSchedule.cs`
- Test: `test/Test/Apps/Diurnal/DiurnalScheduleTests.cs` (new)

**Interfaces:**
- Consumes: `AwtrixSettings.SetBrightness(byte)`, `AwtrixSettings.SetGlobalTextColor(string)` (device keys `BRI`, `TCOL`)
- Produces (used by Task 3):
  - `public sealed record DiurnalEntry(TimeSpan Time, AwtrixSettings Settings)`
  - `public sealed class DiurnalSchedule` with:
    - `static DiurnalSchedule Empty`
    - `static DiurnalSchedule Parse(IReadOnlyDictionary<string, string>? config, ILogger logger)`
    - `IReadOnlyList<DiurnalEntry> Entries`
    - `bool IsEmpty`
    - `AwtrixSettings StateAt(DateTime localNow)`
    - `AwtrixSettings DueBetween(DateTime afterExclusive, DateTime upToInclusive)`
    - `static DateTime TruncateToMinute(DateTime value)`

- [ ] **Step 1: Write the failing tests**

Create `test/Test/Apps/Diurnal/DiurnalScheduleTests.cs`:

```csharp
using AwtrixSharpWeb.Apps.Diurnal;
using AwtrixSharpWeb.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Test.Apps.Diurnal
{
    public class DiurnalScheduleTests
    {
        private static readonly DateTime Day = new(2026, 9, 13, 0, 0, 0, DateTimeKind.Local);

        private static readonly Dictionary<string, string> Shipped = new()
        {
            ["0600"] = "Brightness=8",
            ["0700"] = "GlobalTextColor=#FFFFFF",
            ["1900"] = "GlobalTextColor=#FF0000",
            ["2100"] = "Brightness=1",
        };

        private static DateTime T(int hour, int minute, int dayOffset = 0) => Day.AddDays(dayOffset).Add(new TimeSpan(hour, minute, 0));

        private static DiurnalSchedule Parse(Dictionary<string, string> config) => DiurnalSchedule.Parse(config, NullLogger.Instance);

        private static void VerifyWarningLogged(Mock<ILogger> logger)
        {
            logger.Verify(l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
                Times.AtLeastOnce);
        }

        // ---------- Parse (CR-21) ----------

        [Fact]
        public void Parse_ValidConfig_ProducesSortedEntriesWithDeviceKeys()
        {
            var sut = Parse(new Dictionary<string, string>
            {
                ["2100"] = "Brightness=1",
                ["0600"] = "Brightness=8",
                ["0700"] = "GlobalTextColor=#FFFFFF",
            });

            Assert.Equal(new[] { new TimeSpan(6, 0, 0), new TimeSpan(7, 0, 0), new TimeSpan(21, 0, 0) }, sut.Entries.Select(e => e.Time));
            Assert.Equal("8", sut.Entries[0].Settings["BRI"]);
            Assert.Equal("#FFFFFF", sut.Entries[1].Settings["TCOL"]);
        }

        [Theory]
        [InlineData("Brightness=dim")]
        [InlineData("Brightness=300")]
        [InlineData("Brightness=-1")]
        [InlineData("Brightness=8.5")]
        public void Parse_InvalidBrightness_DropsEntryAndWarns(string value)
        {
            var logger = new Mock<ILogger>();

            var sut = DiurnalSchedule.Parse(new Dictionary<string, string> { ["2100"] = value }, logger.Object);

            Assert.True(sut.IsEmpty);
            VerifyWarningLogged(logger);
        }

        [Fact]
        public void Parse_MixedValidAndInvalidSettings_KeepsValidPart()
        {
            var sut = Parse(new Dictionary<string, string> { ["2100"] = "Brightness=dim;GlobalTextColor=#00FF00" });

            var entry = Assert.Single(sut.Entries);
            Assert.False(entry.Settings.ContainsKey("BRI"));
            Assert.Equal("#00FF00", entry.Settings["TCOL"]);
        }

        [Fact]
        public void Parse_UnknownSettingOnly_DropsEntryAndWarns()
        {
            var logger = new Mock<ILogger>();

            var sut = DiurnalSchedule.Parse(new Dictionary<string, string> { ["0600"] = "Brightnes=8" }, logger.Object);

            Assert.True(sut.IsEmpty);
            VerifyWarningLogged(logger);
        }

        [Theory]
        [InlineData("2400")]
        [InlineData("0660")]
        [InlineData("not-a-time")]
        [InlineData("")]
        public void Parse_InvalidTimeKey_DropsEntryAndWarns(string key)
        {
            var logger = new Mock<ILogger>();

            var sut = DiurnalSchedule.Parse(new Dictionary<string, string> { [key] = "Brightness=5" }, logger.Object);

            Assert.True(sut.IsEmpty);
            VerifyWarningLogged(logger);
        }

        [Fact]
        public void Parse_SettingNamesAreCaseInsensitive()
        {
            var sut = Parse(new Dictionary<string, string> { ["0600"] = "BRIGHTNESS=5; globaltextcolor=#123456" });

            var entry = Assert.Single(sut.Entries);
            Assert.Equal("5", entry.Settings["BRI"]);
            Assert.Equal("#123456", entry.Settings["TCOL"]);
        }

        [Fact]
        public void Parse_NullConfig_IsEmpty()
        {
            Assert.True(DiurnalSchedule.Parse(null, NullLogger.Instance).IsEmpty);
        }

        // ---------- StateAt (CR-20 startup) ----------

        [Fact]
        public void StateAt_0300_UsesYesterdayEveningSettings()
        {
            var state = Parse(Shipped).StateAt(T(3, 0));

            Assert.Equal("1", state["BRI"]);
            Assert.Equal("#FF0000", state["TCOL"]);
        }

        [Fact]
        public void StateAt_0630_CombinesTodayBrightnessWithYesterdayColour()
        {
            var state = Parse(Shipped).StateAt(T(6, 30));

            Assert.Equal("8", state["BRI"]);
            Assert.Equal("#FF0000", state["TCOL"]);
        }

        [Fact]
        public void StateAt_WithinEntryMinute_IncludesThatEntry()
        {
            var state = Parse(Shipped).StateAt(T(7, 0).AddSeconds(42));

            Assert.Equal("8", state["BRI"]);
            Assert.Equal("#FFFFFF", state["TCOL"]);
        }

        [Fact]
        public void StateAt_EmptySchedule_ReturnsEmptySettings()
        {
            Assert.Empty(DiurnalSchedule.Empty.StateAt(T(12, 0)));
        }

        // ---------- DueBetween (CR-20 runtime) ----------

        [Fact]
        public void DueBetween_StallAcrossEntry_AppliesMissedEntryOnly()
        {
            var due = Parse(Shipped).DueBetween(T(20, 59), T(21, 1));

            var setting = Assert.Single(due);
            Assert.Equal("BRI", setting.Key);
            Assert.Equal("1", setting.Value);
        }

        [Fact]
        public void DueBetween_WindowCrossesMidnight_AppliesMidnightEntry()
        {
            var sut = Parse(new Dictionary<string, string> { ["0000"] = "Brightness=3" });

            var due = sut.DueBetween(T(23, 59), T(0, 1, dayOffset: 1));

            Assert.Equal("3", due["BRI"]);
        }

        [Fact]
        public void DueBetween_StartIsExclusiveEndIsInclusive()
        {
            var sut = Parse(Shipped);

            Assert.Empty(sut.DueBetween(T(6, 0), T(6, 59)));
            Assert.Equal("#FFFFFF", sut.DueBetween(T(6, 59), T(7, 0))["TCOL"]);
        }

        [Fact]
        public void DueBetween_SecondsAreIgnored()
        {
            var due = Parse(Shipped).DueBetween(T(5, 59).AddSeconds(30), T(6, 0).AddSeconds(7));

            Assert.Equal("8", due["BRI"]);
        }

        [Fact]
        public void DueBetween_EmptyOrBackwardsWindow_ReturnsEmpty()
        {
            var sut = Parse(Shipped);

            Assert.Empty(sut.DueBetween(T(21, 0), T(21, 0)));
            Assert.Empty(sut.DueBetween(T(21, 30), T(20, 30)));
        }

        [Fact]
        public void DueBetween_SameKeyTwiceInWindow_LaterEntryWins()
        {
            // 0600 BRI=8 then 0700 TCOL white then 1900 TCOL red
            var due = Parse(Shipped).DueBetween(T(5, 0), T(20, 0));

            Assert.Equal("8", due["BRI"]);
            Assert.Equal("#FF0000", due["TCOL"]);
        }

        [Fact]
        public void DueBetween_WindowOfADayOrMore_ReturnsStateAtEnd()
        {
            var sut = Parse(Shipped);

            var due = sut.DueBetween(T(12, 0), T(6, 30, dayOffset: 3));

            Assert.Equal(sut.StateAt(T(6, 30, dayOffset: 3)), due);
        }

        [Fact]
        public void TruncateToMinute_DropsSecondsAndKeepsKind()
        {
            var value = new DateTime(2026, 9, 13, 6, 0, 59, 999, DateTimeKind.Local);

            var truncated = DiurnalSchedule.TruncateToMinute(value);

            Assert.Equal(new DateTime(2026, 9, 13, 6, 0, 0, DateTimeKind.Local), truncated);
            Assert.Equal(DateTimeKind.Local, truncated.Kind);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps.Diurnal.DiurnalScheduleTests"`

Expected: build FAILS with CS0246 (`DiurnalSchedule` not found).

- [ ] **Step 3: Implement**

Create `src/api/Apps/Diurnal/DiurnalSchedule.cs`:

```csharp
using AwtrixSharpWeb.Domain;
using System.Globalization;

namespace AwtrixSharpWeb.Apps.Diurnal
{
    /// <summary>
    /// One validated time-map entry: local time of day and the device settings to apply.
    /// </summary>
    public sealed record DiurnalEntry(TimeSpan Time, AwtrixSettings Settings);

    /// <summary>
    /// The Diurnal time map, parsed and validated once. Pure: no I/O and no clock.
    /// All times are local wall-clock values at minute resolution.
    /// </summary>
    public sealed class DiurnalSchedule
    {
        private const string TimeFormat = "hhmm";
        private const string BrightnessSetting = "brightness";
        private const string GlobalTextColorSetting = "globaltextcolor";

        private readonly List<DiurnalEntry> _entries;

        private DiurnalSchedule(List<DiurnalEntry> entries)
        {
            _entries = entries;
        }

        public static DiurnalSchedule Empty { get; } = new(new List<DiurnalEntry>());

        /// <summary>Entries sorted by time of day.</summary>
        public IReadOnlyList<DiurnalEntry> Entries => _entries;

        public bool IsEmpty => _entries.Count == 0;

        /// <summary>
        /// Parse "HHmm" -> "Name=Value[;Name=Value]" with the invariant culture. Invalid time keys, unknown
        /// setting names and invalid values are logged at Warning and skipped; entries left with no valid
        /// setting are dropped.
        /// </summary>
        public static DiurnalSchedule Parse(IReadOnlyDictionary<string, string>? config, ILogger logger)
        {
            var entries = new List<DiurnalEntry>();
            if (config == null)
            {
                return new DiurnalSchedule(entries);
            }

            foreach (var (key, value) in config)
            {
                var timeKey = key ?? string.Empty;
                if (!TimeSpan.TryParseExact(timeKey.Trim(), TimeFormat, CultureInfo.InvariantCulture, out var time))
                {
                    logger.LogWarning("Diurnal entry '{TimeKey}' ignored: the key must be a 4-digit HHmm time between 0000 and 2359", timeKey);
                    continue;
                }

                var settings = ParseSettings(timeKey, value, logger);
                if (settings.Count == 0)
                {
                    logger.LogWarning("Diurnal entry '{TimeKey}' = '{Value}' ignored: it has no valid settings", timeKey, value);
                    continue;
                }

                entries.Add(new DiurnalEntry(time, settings));
            }

            entries.Sort((a, b) => a.Time.CompareTo(b.Time));
            return new DiurnalSchedule(entries);
        }

        private static AwtrixSettings ParseSettings(string timeKey, string? value, ILogger logger)
        {
            var settings = new AwtrixSettings();
            var parts = (value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var part in parts)
            {
                var pair = part.Split('=', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (pair.Length != 2)
                {
                    logger.LogWarning("Diurnal setting '{Setting}' at {TimeKey} ignored: expected Name=Value", part, timeKey);
                    continue;
                }

                var name = pair[0];
                var settingValue = pair[1];

                switch (name.ToLowerInvariant())
                {
                    case BrightnessSetting:
                        if (byte.TryParse(settingValue, NumberStyles.None, CultureInfo.InvariantCulture, out var brightness))
                        {
                            settings.SetBrightness(brightness);
                        }
                        else
                        {
                            logger.LogWarning("Diurnal Brightness '{Value}' at {TimeKey} ignored: must be a whole number from 0 to 255", settingValue, timeKey);
                        }
                        break;

                    case GlobalTextColorSetting:
                        settings.SetGlobalTextColor(settingValue);
                        break;

                    default:
                        logger.LogWarning("Diurnal setting '{SettingName}' at {TimeKey} ignored: unknown setting (expected Brightness or GlobalTextColor)", name, timeKey);
                        break;
                }
            }

            return settings;
        }

        /// <summary>
        /// Settings in effect at <paramref name="localNow"/>: all entries replayed chronologically over the
        /// preceding 24 hours (yesterday's entries after now, then today's up to and including now);
        /// the last value per setting wins.
        /// </summary>
        public AwtrixSettings StateAt(DateTime localNow)
        {
            var now = TruncateToMinute(localNow).TimeOfDay;
            var result = new AwtrixSettings();

            foreach (var entry in _entries.Where(e => e.Time > now))
            {
                Merge(result, entry.Settings);
            }

            foreach (var entry in _entries.Where(e => e.Time <= now))
            {
                Merge(result, entry.Settings);
            }

            return result;
        }

        /// <summary>
        /// Settings of every entry occurring in (afterExclusive, upToInclusive], merged chronologically
        /// (last value per setting wins). Empty when the window is empty or runs backwards. A window of a
        /// day or more returns <see cref="StateAt"/> of its end.
        /// </summary>
        public AwtrixSettings DueBetween(DateTime afterExclusive, DateTime upToInclusive)
        {
            var from = TruncateToMinute(afterExclusive);
            var to = TruncateToMinute(upToInclusive);
            var result = new AwtrixSettings();

            if (_entries.Count == 0 || to <= from)
            {
                return result;
            }

            if (to - from >= TimeSpan.FromDays(1))
            {
                return StateAt(to);
            }

            for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
            {
                foreach (var entry in _entries)
                {
                    var occurrence = day + entry.Time;
                    if (occurrence > from && occurrence <= to)
                    {
                        Merge(result, entry.Settings);
                    }
                }
            }

            return result;
        }

        public static DateTime TruncateToMinute(DateTime value)
        {
            return new DateTime(value.Ticks - (value.Ticks % TimeSpan.TicksPerMinute), value.Kind);
        }

        private static void Merge(AwtrixSettings target, AwtrixSettings source)
        {
            foreach (var (key, value) in source)
            {
                target[key] = value;
            }
        }
    }
}
```

Note: `ILogger` resolves through the api project's implicit usings (`Microsoft.Extensions.Logging`), the same way `DiurnalApp.cs` uses it today. If the build reports CS0246 for `ILogger`, add `using Microsoft.Extensions.Logging;`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps.Diurnal.DiurnalScheduleTests"`

Expected: PASS.

Then run: `dotnet test`

Expected: 0 failed. `DiurnalApp` is unchanged, so the existing `DiurnalAppTests` still pass.

- [ ] **Step 5: Commit**

```bash
git add src/api/Apps/Diurnal/DiurnalSchedule.cs test/Test/Apps/Diurnal/DiurnalScheduleTests.cs
git commit -m "feat(diurnal): validated DiurnalSchedule with rotation state and due window (CR-20, CR-21)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 3: DiurnalApp — startup rotation and (last, now] ticks (CR-20)

**Files:**
- Modify: `src/api/Apps/Diurnal/DiurnalApp.cs` (whole file)
- Test: `test/Test/Apps/Diurnal/DiurnalAppTests.cs` (**rewrite whole file**)

**Interfaces:**
- Consumes (Task 2):
  - `DiurnalSchedule.Parse(IReadOnlyDictionary<string,string>?, ILogger)`
  - `StateAt(DateTime)`, `DueBetween(DateTime, DateTime)`, `IsEmpty`, `Empty`, `TruncateToMinute(DateTime)`
- Consumes (WS1/WS3): `FireAndLog(Func<Task>, string)`, `Set(AwtrixSettings)`, `IClock.Now`, `ITimerService.MinuteChanged`, `IAwtrixApp.InitAsync()`
- Produces: none. The public constructor signature does not change.

**Why the test file is rewritten:**
- Startup now restores yesterday's carry-over state, so the old `Times.Once` counts no longer hold.
- Several old tests used `DateTime.Today`, which makes a (last, now] window date-dependent and flaky.

Every old test's intent is preserved:

| Old test | New test |
|---|---|
| `Init_WithEmptyConfig_DoesNotThrow` | `InitAsync_EmptyConfig_DoesNotThrowSubscribeOrPublish` |
| `Init_WithUnparsableTimeKey_DoesNotThrow` | `InitAsync_UnparsableTimeKey_DoesNotThrowOrPublish` |
| `Init_WithUnknownSettingKeyOnly_ParsesWithoutThrowing`, `Init_AfterEntryWithUnknownKey_StartupReplayDoesNotThrow` | `InitAsync_UnknownSettingKeyOnly_DoesNotThrowOrPublish` |
| `MinuteChanged_TimeWithOnlyUnknownSettingKey_DoesNotThrowAndSkipsSet`, `MinuteTick_UnknownKeyOnly_SkipsSetWithoutThrowing` | `MinuteTick_UnknownKeyOnly_DoesNotThrowOrPublish` |
| `MinuteTick_OutOfRangeBrightness_DoesNotThrowAndDoesNotPublish` | `MinuteTick_OutOfRangeBrightness_DoesNotThrowOrPublish` |
| `MinuteChanged_MatchingBrightnessEntry_AppliesBrightness`, `MinuteTick_MatchingEntry_AppliesSettings` | `MinuteTick_MatchingBrightnessEntry_AppliesBrightness` |
| `MinuteChanged_MatchingColorEntry_AppliesGlobalTextColor` | `MinuteTick_MatchingColorEntry_AppliesGlobalTextColor` |
| `MinuteChanged_CompoundEntry_AppliesBothSettingsTogether` | `MinuteTick_CompoundEntry_AppliesBothSettingsTogether` |
| `MinuteChanged_NonMatchingTime_DoesNotApplySettings` | `MinuteTick_NonMatchingTime_DoesNotPublish` |
| `MinuteTick_WithNonZeroSeconds_StillMatchesEntry` | same name |
| `MinuteTick_WhenSetFaults_DoesNotPropagate` | same name |
| `Init_ReplaysEarlierEntriesUsingInjectedClock` | `InitAsync_RestoresStateInEffect_AsOneMergedSet` |

- [ ] **Step 1: Write the failing tests**

Replace the whole of `test/Test/Apps/Diurnal/DiurnalAppTests.cs`:

```csharp
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.Diurnal;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Test.Apps.Diurnal
{
    /// <summary>
    /// DiurnalApp over a mocked timer and awtrix service, with fixed dates (never DateTime.Today).
    /// Moq's completed tasks make FireAndLog run synchronously, so assertions can follow the raise directly.
    /// </summary>
    public class DiurnalAppTests
    {
        private static readonly TimeSpan Aest = TimeSpan.FromHours(10);
        private static readonly DateTime Day = new(2026, 9, 13, 0, 0, 0, DateTimeKind.Local);

        private static readonly (string Time, string Value)[] Shipped =
        {
            ("0600", "Brightness=8"),
            ("0700", "GlobalTextColor=#FFFFFF"),
            ("1900", "GlobalTextColor=#FF0000"),
            ("2100", "Brightness=1"),
        };

        private readonly Mock<ITimerService> _timer = new();
        private readonly Mock<IAwtrixService> _awtrix = new();
        private readonly AwtrixAddress _address = new() { BaseTopic = "awtrix/clock1" };
        private readonly List<AwtrixSettings> _applied = new();

        public DiurnalAppTests()
        {
            _awtrix.Setup(a => a.Set(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixSettings>()))
                .Callback<AwtrixAddress, AwtrixSettings>((_, settings) => _applied.Add(settings))
                .ReturnsAsync(true);
            _awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>())).ReturnsAsync(true);
        }

        private static DateTimeOffset At(int hour, int minute) => new(2026, 9, 13, hour, minute, 0, Aest);

        private DiurnalApp CreateApp(DateTimeOffset now, params (string Time, string Value)[] entries)
        {
            var config = AppConfig.Empty().WithName(AppNames.DiurnalApp);
            foreach (var (time, value) in entries)
            {
                config.Config[time] = value;
            }

            return new DiurnalApp(NullLogger.Instance, new MockClock(now), _timer.Object, config, _address, _awtrix.Object);
        }

        /// <summary>Init at <paramref name="now"/>, then forget the startup publish.</summary>
        private async Task<DiurnalApp> StartApp(DateTimeOffset now, params (string Time, string Value)[] entries)
        {
            var app = CreateApp(now, entries);
            await app.InitAsync();
            _applied.Clear();
            return app;
        }

        private void RaiseMinute(int hour, int minute, int second = 0, int dayOffset = 0)
        {
            _timer.Raise(t => t.MinuteChanged += null, _timer.Object,
                new ClockTickEventArgs(Day.AddDays(dayOffset).Add(new TimeSpan(hour, minute, second))));
        }

        // ---------- Init / validation (CR-21) ----------

        [Fact]
        public async Task InitAsync_EmptyConfig_DoesNotThrowSubscribeOrPublish()
        {
            var app = CreateApp(At(7, 0));

            var ex = await Record.ExceptionAsync(() => app.InitAsync());

            Assert.Null(ex);
            Assert.Empty(_applied);
            _timer.VerifyAdd(t => t.MinuteChanged += It.IsAny<EventHandler<ClockTickEventArgs>>(), Times.Never);
        }

        [Fact]
        public async Task InitAsync_UnparsableTimeKey_DoesNotThrowOrPublish()
        {
            var app = CreateApp(At(7, 0), ("not-a-time", "Brightness=5"));

            var ex = await Record.ExceptionAsync(() => app.InitAsync());

            Assert.Null(ex);
            Assert.Empty(_applied);
        }

        [Fact]
        public async Task InitAsync_UnknownSettingKeyOnly_DoesNotThrowOrPublish()
        {
            var app = CreateApp(At(7, 0), ("0600", "Brightnes=8"));

            var ex = await Record.ExceptionAsync(() => app.InitAsync());

            Assert.Null(ex);
            Assert.Empty(_applied);
        }

        [Theory]
        [InlineData("Brightness=dim")]
        [InlineData("Brightness=300")]
        public async Task InitAsync_InvalidBrightnessValue_DoesNotThrowOrPublish(string value)
        {
            var app = CreateApp(At(22, 0), ("2100", value));

            var ex = await Record.ExceptionAsync(() => app.InitAsync());

            Assert.Null(ex);
            Assert.Empty(_applied);
        }

        [Fact]
        public async Task InitAsync_MixedValidAndInvalidSettings_AppliesValidPart()
        {
            var app = CreateApp(At(22, 0), ("2100", "Brightness=dim;GlobalTextColor=#00FF00"));

            await app.InitAsync();

            var settings = Assert.Single(_applied);
            Assert.False(settings.ContainsKey("BRI"));
            Assert.Equal("#00FF00", settings["TCOL"]);
        }

        [Fact]
        public async Task MinuteTick_UnknownKeyOnly_DoesNotThrowOrPublish()
        {
            await StartApp(At(5, 59), ("0600", "SomeUnknownSetting=123"));

            var ex = Record.Exception(() => RaiseMinute(6, 0));

            Assert.Null(ex);
            Assert.Empty(_applied);
        }

        [Fact]
        public async Task MinuteTick_OutOfRangeBrightness_DoesNotThrowOrPublish()
        {
            var app = CreateApp(At(20, 59), ("2100", "Brightness=300"));
            await app.InitAsync();

            var ex = Record.Exception(() => RaiseMinute(21, 0));

            Assert.Null(ex);
            Assert.Empty(_applied);
        }

        // ---------- Startup state (CR-20) ----------

        [Fact]
        public async Task InitAsync_RestoresStateInEffect_AsOneMergedSet()
        {
            var app = CreateApp(At(7, 0), Shipped);

            await app.InitAsync();

            var settings = Assert.Single(_applied);
            Assert.Equal("8", settings["BRI"]);
            Assert.Equal("#FFFFFF", settings["TCOL"]);
            _awtrix.Verify(a => a.Set(_address, It.IsAny<AwtrixSettings>()), Times.Once);
        }

        [Fact]
        public async Task InitAsync_RestartAt0300_RestoresYesterdayEveningSettings()
        {
            var app = CreateApp(At(3, 0), Shipped);

            await app.InitAsync();

            var settings = Assert.Single(_applied);
            Assert.Equal("1", settings["BRI"]);
            Assert.Equal("#FF0000", settings["TCOL"]);
        }

        [Fact]
        public async Task MinuteTick_SameMinuteAsStartup_DoesNotReapply()
        {
            var app = CreateApp(At(6, 0), ("0600", "Brightness=8"));
            await app.InitAsync();
            Assert.Single(_applied);

            RaiseMinute(6, 0, second: 30);

            Assert.Single(_applied);
        }

        // ---------- Ticks (CR-20) ----------

        [Fact]
        public async Task MinuteTick_MatchingBrightnessEntry_AppliesBrightness()
        {
            await StartApp(At(5, 59), ("0600", "Brightness=8"));

            RaiseMinute(6, 0);

            Assert.Equal("8", Assert.Single(_applied)["BRI"]);
            _awtrix.Verify(a => a.Set(_address, It.IsAny<AwtrixSettings>()), Times.Exactly(2)); // startup + tick
        }

        [Fact]
        public async Task MinuteTick_MatchingColorEntry_AppliesGlobalTextColor()
        {
            await StartApp(At(21, 59), ("2200", "GlobalTextColor=#112233"));

            RaiseMinute(22, 0);

            Assert.Equal("#112233", Assert.Single(_applied)["TCOL"]);
        }

        [Fact]
        public async Task MinuteTick_CompoundEntry_AppliesBothSettingsTogether()
        {
            await StartApp(At(6, 59), ("0700", "Brightness=5;GlobalTextColor=#FFFFFF"));

            RaiseMinute(7, 0);

            var settings = Assert.Single(_applied);
            Assert.Equal("5", settings["BRI"]);
            Assert.Equal("#FFFFFF", settings["TCOL"]);
        }

        [Fact]
        public async Task MinuteTick_NonMatchingTime_DoesNotPublish()
        {
            await StartApp(At(8, 59), ("0600", "Brightness=8"));

            RaiseMinute(9, 0);

            Assert.Empty(_applied);
        }

        [Fact]
        public async Task MinuteTick_WithNonZeroSeconds_StillMatchesEntry()
        {
            await StartApp(At(5, 59), ("0600", "Brightness=8"));

            RaiseMinute(6, 0, second: 7);

            Assert.Equal("8", Assert.Single(_applied)["BRI"]);
        }

        [Fact]
        public async Task MinuteTick_WhenSetFaults_DoesNotPropagate()
        {
            _awtrix.Setup(a => a.Set(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixSettings>()))
                .ThrowsAsync(new HttpRequestException("device offline"));
            var app = CreateApp(At(5, 59), ("0600", "Brightness=8"));

            var initException = await Record.ExceptionAsync(() => app.InitAsync());
            var tickException = Record.Exception(() => RaiseMinute(6, 0));

            Assert.Null(initException);
            Assert.Null(tickException);
        }

        [Fact]
        public async Task MinuteTick_StallAcrossEntry_AppliesMissedEntry()
        {
            await StartApp(At(20, 59), Shipped);

            RaiseMinute(21, 1); // coalesced tick: 21:00 never arrived

            var settings = Assert.Single(_applied);
            Assert.Equal("1", Assert.Single(settings, kv => kv.Key == "BRI").Value);
            Assert.Single(settings);
        }

        [Fact]
        public async Task MinuteTick_CrossingMidnight_AppliesMidnightEntry()
        {
            await StartApp(At(23, 59), ("0000", "Brightness=3"));

            RaiseMinute(0, 1, dayOffset: 1);

            Assert.Equal("3", Assert.Single(_applied)["BRI"]);
        }

        [Fact]
        public async Task MinuteTick_ClockMovesBackwards_SkipsThenResumesFromNewTime()
        {
            await StartApp(At(3, 0), ("0230", "Brightness=2"));

            RaiseMinute(2, 0); // DST end / NTP step back
            Assert.Empty(_applied);

            RaiseMinute(2, 30);
            Assert.Equal("2", Assert.Single(_applied)["BRI"]);
        }

        [Fact]
        public async Task MinuteTick_AfterGapOfDays_AppliesStateAtTickTime()
        {
            await StartApp(At(0, 30), ("0600", "Brightness=8"), ("2100", "Brightness=1"));

            RaiseMinute(7, 0, dayOffset: 3); // host suspended for days

            Assert.Equal("8", Assert.Single(_applied)["BRI"]);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps.Diurnal.DiurnalAppTests"`

Expected: FAIL. At least these fail against the old implementation:
- `InitAsync_RestartAt0300_RestoresYesterdayEveningSettings`
- `InitAsync_RestoresStateInEffect_AsOneMergedSet` (two Sets)
- `MinuteTick_StallAcrossEntry_AppliesMissedEntry`
- `MinuteTick_CrossingMidnight_AppliesMidnightEntry`
- `MinuteTick_AfterGapOfDays_AppliesStateAtTickTime`
- `InitAsync_EmptyConfig_DoesNotThrowSubscribeOrPublish` (the old code subscribes)

- [ ] **Step 3: Implement**

Replace the whole of `src/api/Apps/Diurnal/DiurnalApp.cs`. If Task 0 recorded an async hook, apply its adaptation rule to `Initialize`.

```csharp
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;

namespace AwtrixSharpWeb.Apps.Diurnal
{
    /// <summary>
    /// Change brightness and colour based on time of day.
    /// At startup the settings in effect now (including yesterday's carry-over) are restored in one publish;
    /// afterwards every entry in (last processed minute, current minute] is applied, so stalls, coalesced
    /// ticks, suspend and DST gaps never skip a setting.
    /// </summary>
    public class DiurnalApp : AwtrixApp<AppConfig>
    {
        private readonly ITimerService _timerService;
        private readonly IClock _clock;
        private readonly object _gate = new();

        private DiurnalSchedule _schedule = DiurnalSchedule.Empty;
        private DateTime _lastProcessed;

        public DiurnalApp(
            ILogger logger
            , IClock clock
            , ITimerService timerService
            , AppConfig config
            , AwtrixAddress awtrixAddress
            , IAwtrixService awtrixService)
            : base(logger, config, awtrixAddress, awtrixService)
        {
            _clock = clock;
            _timerService = timerService;
        }

        protected override void Initialize()
        {
            _schedule = DiurnalSchedule.Parse(Config.Config, Logger);

            if (_schedule.IsEmpty)
            {
                Logger.LogWarning("DiurnalApp on {BaseTopic} has no valid time entries; nothing will be scheduled. Check its Config in appsettings.json", AwtrixAddress.BaseTopic);
                return;
            }

            Logger.LogDebug("DiurnalApp on {BaseTopic} initialised with {Count} time entries", AwtrixAddress.BaseTopic, _schedule.Entries.Count);

            // IClock is backed by TimeProvider.GetLocalNow(): DateTime is the local wall-clock time
            var now = _clock.Now.DateTime;
            lock (_gate)
            {
                _lastProcessed = DiurnalSchedule.TruncateToMinute(now);
            }

            _timerService.MinuteChanged += ClockTickMinute;

            var state = _schedule.StateAt(now);
            Logger.LogInformation("{BaseTopic}: restoring Diurnal settings in effect at {Time:HH:mm}: {AwtrixSetting}", AwtrixAddress.BaseTopic, now, state);
            _ = FireAndLog(() => Set(state), "DiurnalStartupRestore");
        }

        private void ClockTickMinute(object? sender, ClockTickEventArgs e)
        {
            // e.Time is local wall-clock time (TimerService contract)
            _ = FireAndLog(() => ApplyDueAsync(e.Time), nameof(ClockTickMinute));
        }

        private async Task ApplyDueAsync(DateTime tickTime)
        {
            var now = DiurnalSchedule.TruncateToMinute(tickTime);
            DateTime from;

            lock (_gate)
            {
                from = _lastProcessed;
                _lastProcessed = now;
            }

            if (now < from)
            {
                Logger.LogInformation("{BaseTopic}: clock moved back from {From:HH:mm} to {Now:HH:mm}; Diurnal resumes from the new time", AwtrixAddress.BaseTopic, from, now);
                return;
            }

            var due = _schedule.DueBetween(from, now);
            if (due.Count == 0)
            {
                return;
            }

            Logger.LogInformation("{BaseTopic} @ {Time:HH:mm}: Applying global setting {AwtrixSetting}", AwtrixAddress.BaseTopic, now, due);
            await Set(due);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps.Diurnal"`

Expected: PASS for both `DiurnalAppTests` and `DiurnalScheduleTests`.

Then run: `dotnet test`

Expected: 0 failed. If a `ConductorTests` case asserted Diurnal call counts, it should be unaffected: Conductor tests use their own config. Investigate any failure rather than weakening assertions.

Then run: `grep -n "DateTime.Now\|DateTime.Today\|\.Result\|\.Wait()" src/api/Apps/Diurnal/*.cs`

Expected: no output.

- [ ] **Step 5: Commit**

```bash
git add src/api/Apps/Diurnal/DiurnalApp.cs test/Test/Apps/Diurnal/DiurnalAppTests.cs
git commit -m "fix(diurnal): restore 24h rotation state at startup and apply every entry in (last, now] (CR-20)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 4: SlackStatusApp — null-safe user id and status, async handler (CR-24)

**Files:**
- Modify: `src/api/Apps/SlackStatus/SlackStatusApp.cs` (whole file)
- Test: `test/Test/Apps/SlackStatus/SlackStatusAppTests.cs` (**rewrite whole file**)

**Interfaces:**
- Consumes (WS1/WS3):
  - `ISlackConnector.UserStatusChanged` (`EventHandler<SlackUserStatusChangedEventArgs>`)
  - `IAwtrixService.AppUpdate(AwtrixAddress, string, AwtrixAppMessage)`, `AppClear(AwtrixAddress, string)`
  - `FireAndLog`, `IAwtrixApp.InitAsync()`
- Produces:
  - `public const string UserIdConfigKey = "SlackUserId"`
  - `public const string UserIdEnvironmentVariable = "AWTRIXSHARP_SLACK__USERID"`
  - The constructor does not change.

**Why the test file is rewritten:** the old file built a real `MqttConnector`/`HttpPublisher` chain and raised the event by reflection, because the app used to take concrete types. WS1 moved it to `IAwtrixService`/`ISlackConnector`, so mocks now cover every branch. Old intents are preserved:

| Old test | New test |
|---|---|
| `Init_DoesNotThrow_AndSubscribesToConnector` | `InitAsync_WithUserId_SubscribesOnce` |
| `UserStatusChanged_ForDifferentUser_IsIgnored` | same name |
| `UserStatusChanged_ForTrackedUser_WithEmptyStatus_ClearsWithoutThrowing` | `UserStatusChanged_TrackedUserEmptyOrNullStatus_ClearsAndDoesNotPublish` |

- [ ] **Step 1: Write the failing tests**

Replace the whole of `test/Test/Apps/SlackStatus/SlackStatusAppTests.cs`:

```csharp
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.SlackStatus;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Test.Apps.SlackStatus
{
    /// <summary>
    /// SlackStatusApp over mocked ISlackConnector / IAwtrixService. Moq's completed tasks make FireAndLog
    /// run synchronously, so assertions can follow the raise directly.
    /// </summary>
    public class SlackStatusAppTests
    {
        private const string UserIdEnvVar = "AWTRIXSHARP_SLACK__USERID";
        private const string AppName = "SlackStatusApp";

        private readonly Mock<ISlackConnector> _slack = new();
        private readonly Mock<IAwtrixService> _awtrix = new();
        private readonly AwtrixAddress _address = new() { BaseTopic = "awtrix/clock1" };
        private readonly List<AwtrixAppMessage> _published = new();

        public SlackStatusAppTests()
        {
            _awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>())).ReturnsAsync(true);
            _awtrix.Setup(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()))
                .Callback<AwtrixAddress, string, AwtrixAppMessage>((_, _, message) => _published.Add(message))
                .ReturnsAsync(true);
        }

        private SlackStatusApp CreateApp(string trackingUserId, params ValueMap[] valueMaps)
        {
            var config = new SlackStatusAppConfig { Type = AppName };
            config.Config["SlackUserId"] = trackingUserId;
            config.ValueMaps = valueMaps.ToList();
            return new SlackStatusApp(NullLogger.Instance, config, _address, _awtrix.Object, _slack.Object);
        }

        /// <summary>Init with a tracked user id, then forget the init-time AppClear.</summary>
        private async Task<SlackStatusApp> StartApp(string trackingUserId = "U123", params ValueMap[] valueMaps)
        {
            var app = CreateApp(trackingUserId, valueMaps);
            await app.InitAsync();
            _awtrix.Invocations.Clear();
            _published.Clear();
            return app;
        }

        private void Raise(SlackUserStatusChangedEventArgs args)
        {
            _slack.Raise(s => s.UserStatusChanged += null, _slack.Object, args);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task InitAsync_NoUserIdConfigured_DoesNotThrowOrSubscribe(string userId)
        {
            var previous = Environment.GetEnvironmentVariable(UserIdEnvVar);
            Environment.SetEnvironmentVariable(UserIdEnvVar, null);
            try
            {
                var app = CreateApp(userId);

                var ex = await Record.ExceptionAsync(() => app.InitAsync());

                Assert.Null(ex);
                _slack.VerifyAdd(s => s.UserStatusChanged += It.IsAny<EventHandler<SlackUserStatusChangedEventArgs>>(), Times.Never);
            }
            finally
            {
                Environment.SetEnvironmentVariable(UserIdEnvVar, previous);
            }
        }

        [Fact]
        public async Task InitAsync_WithUserId_SubscribesOnce()
        {
            var app = CreateApp("U123");

            await app.InitAsync();

            _slack.VerifyAdd(s => s.UserStatusChanged += It.IsAny<EventHandler<SlackUserStatusChangedEventArgs>>(), Times.Once);
        }

        [Fact]
        public async Task UserStatusChanged_ForDifferentUser_IsIgnored()
        {
            await StartApp("U123");

            var ex = Record.Exception(() => Raise(new SlackUserStatusChangedEventArgs { UserId = "SomeoneElse", StatusText = "In a meeting" }));

            Assert.Null(ex);
            Assert.Empty(_awtrix.Invocations);
        }

        [Fact]
        public async Task UserStatusChanged_NullUserId_IsIgnored()
        {
            await StartApp("U123");

            var ex = Record.Exception(() => Raise(new SlackUserStatusChangedEventArgs { UserId = null!, StatusText = "In a meeting" }));

            Assert.Null(ex);
            Assert.Empty(_awtrix.Invocations);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public async Task UserStatusChanged_TrackedUserEmptyOrNullStatus_ClearsAndDoesNotPublish(string? statusText)
        {
            await StartApp("U123");

            Raise(new SlackUserStatusChangedEventArgs { UserId = "U123", StatusText = statusText!, StatusEmoji = ":coffee:" });

            _awtrix.Verify(a => a.AppClear(_address, AppName), Times.Once);
            _awtrix.Verify(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()), Times.Never);
        }

        [Fact]
        public async Task UserStatusChanged_TrackedUserTextWithoutMap_PublishesTextWithDefaultDuration()
        {
            await StartApp("U123");

            Raise(new SlackUserStatusChangedEventArgs { UserId = "U123", StatusText = "In a meeting", StatusEmoji = null! });

            var message = Assert.Single(_published);
            Assert.Equal("In a meeting", message.Text);
            Assert.Equal("50", message["duration"]);
            _awtrix.Verify(a => a.AppUpdate(_address, AppName, It.IsAny<AwtrixAppMessage>()), Times.Once);
        }

        [Fact]
        public async Task UserStatusChanged_TextMatchesValueMap_DecoratesMessage()
        {
            var busy = new ValueMap { { "ValueMatcher", "busy" }, { "Text", "Busy" }, { "Icon", "38789" } };
            await StartApp("U123", busy);

            Raise(new SlackUserStatusChangedEventArgs { UserId = "U123", StatusText = "very busy", StatusEmoji = ":x:" });

            var message = Assert.Single(_published);
            Assert.Equal("Busy", message.Text);
            Assert.Equal("38789", message["icon"]);
        }

        [Fact]
        public async Task UserStatusChanged_EmojiMatchesValueMapWhenTextDoesNot_DecoratesMessage()
        {
            var calendar = new ValueMap { { "ValueMatcher", ":spiral_calendar_pad:" }, { "Icon", "1234" } };
            await StartApp("U123", calendar);

            Raise(new SlackUserStatusChangedEventArgs { UserId = "U123", StatusText = "Planning", StatusEmoji = ":spiral_calendar_pad:" });

            Assert.Equal("1234", Assert.Single(_published)["icon"]);
        }

        [Fact]
        public async Task UserStatusChanged_WhenAppUpdateFaults_DoesNotPropagate()
        {
            _awtrix.Setup(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()))
                .ThrowsAsync(new HttpRequestException("device offline"));
            await StartApp("U123");

            var ex = Record.Exception(() => Raise(new SlackUserStatusChangedEventArgs { UserId = "U123", StatusText = "In a meeting" }));

            Assert.Null(ex);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps.SlackStatus"`

Expected: FAIL. At least these fail:
- `InitAsync_NoUserIdConfigured_DoesNotThrowOrSubscribe` (the old code subscribes first)
- `UserStatusChanged_TrackedUserEmptyOrNullStatus_ClearsAndDoesNotPublish(null)` (the old code publishes `text=null`)
- `UserStatusChanged_WhenAppUpdateFaults_DoesNotPropagate` (`.Result` throws `AggregateException`)

- [ ] **Step 3: Implement**

Replace the whole of `src/api/Apps/SlackStatus/SlackStatusApp.cs`. If Task 0 recorded an async hook, apply its adaptation rule to `Initialize`.

```csharp
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;

namespace AwtrixSharpWeb.Apps.SlackStatus
{
    /// <summary>
    /// Mirror your slack status to the Awtrix
    /// </summary>
    public class SlackStatusApp : AwtrixApp<SlackStatusAppConfig>
    {
        public const string UserIdConfigKey = "SlackUserId";
        public const string UserIdEnvironmentVariable = "AWTRIXSHARP_SLACK__USERID";

        private const int DefaultDurationSeconds = 50;

        private readonly ISlackConnector _slackConnector;
        private string? _trackingUserId;

        public SlackStatusApp(
            ILogger logger
            , SlackStatusAppConfig config
            , AwtrixAddress awtrixAddress
            , IAwtrixService awtrixService
            , ISlackConnector slackConnector) : base(logger, config, awtrixAddress, awtrixService)
        {
            _slackConnector = slackConnector;
        }

        protected override void Initialize()
        {
            var userId = Config.Config.Get(UserIdConfigKey, UserIdEnvironmentVariable);

            if (string.IsNullOrWhiteSpace(userId))
            {
                Logger.LogWarning(
                    "SlackStatusApp on {BaseTopic}: no Slack user id configured (Config:{ConfigKey} or {EnvironmentVariable}); Slack status will not be shown",
                    AwtrixAddress.BaseTopic, UserIdConfigKey, UserIdEnvironmentVariable);
                return;
            }

            _trackingUserId = userId.Trim();
            _slackConnector.UserStatusChanged += UserStatusChanged;
            Logger.LogInformation("Slack monitoring userId='{SlackUserId}'", _trackingUserId);
        }

        private void UserStatusChanged(object? sender, SlackUserStatusChangedEventArgs e)
        {
            if (e is null || !string.Equals(_trackingUserId, e.UserId, StringComparison.Ordinal))
            {
                return;
            }

            // Never block or throw on SlackNet's dispatch thread
            _ = FireAndLog(() => ShowStatusAsync(e), nameof(UserStatusChanged));
        }

        private async Task ShowStatusAsync(SlackUserStatusChangedEventArgs e)
        {
            Logger.LogInformation("SlackApp: {SlackStatus}", e);

            if (string.IsNullOrEmpty(e.StatusText))
            {
                Logger.LogInformation("Clearing status");
                await AppClear();
                return;
            }

            var message = new AwtrixAppMessage();

            // Look for a matching value map on the text, then the emoji
            if (!DecorateIfMatch(e.StatusText, message) && !DecorateIfMatch(e.StatusEmoji, message))
            {
                // No mapping found, use default behavior
                message.SetText(e.StatusText);
                message.SetDuration(DefaultDurationSeconds);
            }

            Logger.LogInformation("Slack status message: {Message}", message);
            await AppUpdate(message);
        }

        private bool DecorateIfMatch(string? value, AwtrixAppMessage message)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            var valueMap = Config.FindMatchingValueMap(value);
            if (valueMap == null)
            {
                return false;
            }

            Logger.LogInformation("ValueMap matched for '{Value}'", value);
            valueMap.Decorate(message, Logger);
            return true;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps.SlackStatus"`

Expected: PASS.

Then run: `dotnet test`

Expected: 0 failed.

Then run: `grep -n "\.Result\|\.Wait()" src/api/Apps/SlackStatus/SlackStatusApp.cs`

Expected: no output.

- [ ] **Step 5: Commit**

```bash
git add src/api/Apps/SlackStatus/SlackStatusApp.cs test/Test/Apps/SlackStatus/SlackStatusAppTests.cs
git commit -m "fix(slack): null-safe user id and status with async non-throwing handler (CR-24)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 5: AwtrixAppMessage — JSON arrays and invariant culture (CR-34, message side)

**Files:**
- Modify: `src/api/Domain/AwtrixAppMessage.cs` (whole file)
- Test: `test/Test/Domain/AwtrixAppMessageJsonTests.cs` (new)

**Interfaces:**
- Consumes: nothing new.
- Produces (used by Task 6):
  - `internal static bool AwtrixAppMessage.TryParseIntArray(string? value, out int[] result)`: comma-separated integers, trimmed, at least 1, invariant culture.
  - `internal static bool AwtrixAppMessage.TryParseIntMatrix(string? value, out int[][] result)`: `;`-separated rows, each a valid int array, at least 1 row.
  - All public setter signatures and stored string values are unchanged.

- [ ] **Step 1: Write the failing tests**

Create `test/Test/Domain/AwtrixAppMessageJsonTests.cs`:

```csharp
using AwtrixSharpWeb.Domain;
using System.Globalization;

namespace Test.Domain
{
    /// <summary>
    /// CR-34: Awtrix expects arrays for line/bar/progressC/progressBC/gradient, and numbers must not follow
    /// the host culture. Stored dictionary values stay unchanged (see AwtrixAppMessageBuilderTests).
    /// </summary>
    public class AwtrixAppMessageJsonTests
    {
        [Fact]
        public void ToJson_Line_EmitsJsonArray()
        {
            var message = new AwtrixAppMessage().SetLine(new[] { 1, 2, 3 });

            Assert.Equal("{\"line\":[1,2,3]}", message.ToJson());
            Assert.Equal("1,2,3", message["line"]);
        }

        [Fact]
        public void ToJson_Bar_EmitsJsonArray()
        {
            var message = new AwtrixAppMessage().SetBar(new[] { 4, -5, 6, 7 });

            Assert.Equal("{\"bar\":[4,-5,6,7]}", message.ToJson());
        }

        [Fact]
        public void ToJson_ProgressColours_EmitJsonArrays()
        {
            var message = new AwtrixAppMessage()
                .SetProgressC(new[] { 255, 0, 0 })
                .SetProgressBC(new[] { 0, 0, 255 });

            Assert.Equal("{\"progressC\":[255,0,0],\"progressBC\":[0,0,255]}", message.ToJson());
        }

        [Fact]
        public void ToJson_Gradient_EmitsNestedJsonArrays()
        {
            var message = new AwtrixAppMessage().SetGradient(new[] { new[] { 255, 0, 0 }, new[] { 0, 255, 0 } });

            Assert.Equal("{\"gradient\":[[255,0,0],[0,255,0]]}", message.ToJson());
        }

        [Fact]
        public void ToJson_UnparsableArrayValue_FallsBackToString()
        {
            var message = new AwtrixAppMessage();
            message["bar"] = "not,numbers";

            Assert.Equal("{\"bar\":\"not,numbers\"}", message.ToJson());
        }

        [Fact]
        public void ToJson_ScalarsRemainStrings()
        {
            // Deliberately unchanged by WS5 (spec non-goal): scalars are still emitted as JSON strings.
            var message = new AwtrixAppMessage().SetDuration(5).SetRainbow();

            Assert.Equal("{\"duration\":\"5\",\"rainbow\":\"true\"}", message.ToJson());
        }

        [Fact]
        public void DoubleSetters_UnderCommaDecimalCulture_UseInvariantFormat()
        {
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                var message = new AwtrixAppMessage().SetBlinkText(0.5).SetFadeText(1.5);

                Assert.Equal("0.5", message["blinkText"]);
                Assert.Equal("1.5", message["fadeText"]);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Theory]
        [InlineData("1,2,3", new[] { 1, 2, 3 })]
        [InlineData(" 7 , 8 ", new[] { 7, 8 })]
        [InlineData("-1", new[] { -1 })]
        public void TryParseIntArray_Valid_ReturnsValues(string input, int[] expected)
        {
            Assert.True(AwtrixAppMessage.TryParseIntArray(input, out var result));
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1,,2")]
        [InlineData("1,a")]
        [InlineData("1.5")]
        public void TryParseIntArray_Invalid_ReturnsFalse(string? input)
        {
            Assert.False(AwtrixAppMessage.TryParseIntArray(input, out _));
        }

        [Fact]
        public void TryParseIntMatrix_ValidAndInvalid()
        {
            Assert.True(AwtrixAppMessage.TryParseIntMatrix("255,0,0;0,255,0", out var matrix));
            Assert.Equal(new[] { new[] { 255, 0, 0 }, new[] { 0, 255, 0 } }, matrix);

            Assert.False(AwtrixAppMessage.TryParseIntMatrix("255,0,0;red", out _));
            Assert.False(AwtrixAppMessage.TryParseIntMatrix("", out _));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Domain.AwtrixAppMessageJsonTests"`

Expected: build FAILS with CS0117. `TryParseIntArray`/`TryParseIntMatrix` do not exist yet.

- [ ] **Step 3: Implement**

Replace the whole of `src/api/Domain/AwtrixAppMessage.cs`:

```csharp
using System.Globalization;
using System.Text.Json;

namespace AwtrixSharpWeb.Domain
{

    /// <summary>
    /// Dictionary-based implementation of an Awtrix application message
    /// that stores all properties as string key-value pairs without default values.
    /// Numbers are always formatted with the invariant culture. Array-valued keys are stored as
    /// comma-separated strings ("1,2,3"; gradient "255,0,0;0,255,0") and emitted as JSON arrays by <see cref="ToJson"/>.
    /// </summary>
    public class AwtrixAppMessage : Dictionary<string, string>
    {
        private const string TextKey = "text";
        private const string GradientKey = "gradient";

        private static readonly HashSet<string> IntArrayKeys = new(StringComparer.Ordinal)
        {
            "line", "bar", "progressC", "progressBC"
        };

        private string Get(string key)
        {
            if (this.TryGetValue(key, out var value))
            {
                return value;
            }
            return null;
        }

        public string Text => Get(TextKey);



        public AwtrixAppMessage SetText(string value)
        {
            this[TextKey] = value;
            return this;
        }

        public AwtrixAppMessage SetTextCase(int value) => SetInt("textCase", value);

        public AwtrixAppMessage SetTopText(bool value)
        {
            return Set("topText", value);
        }


        public AwtrixAppMessage SetHold(bool value = true)
        {
            return Set("hold", value);
        }

        public AwtrixAppMessage SetStack(bool value = true)
        {
            return Set("stack", value);
        }


        public AwtrixAppMessage SetTextOffset(int value) => SetInt("textOffset", value);

        public AwtrixAppMessage SetCenter(bool value)
        {
            return Set("center", value);
        }
        public AwtrixAppMessage SetColor(string value)
        {
            this["color"] = value;
            return this;
        }

        public AwtrixAppMessage SetGradient(int[][] value)
        {
            if (value != null && value.Length > 0)
            {
                this[GradientKey] = string.Join(';', value.Select(JoinInts));
            }
            return this;
        }

        public AwtrixAppMessage SetBlinkText(double value)
        {
            this["blinkText"] = value.ToString(CultureInfo.InvariantCulture);
            return this;
        }

        public AwtrixAppMessage SetFadeText(double value)
        {
            this["fadeText"] = value.ToString(CultureInfo.InvariantCulture);
            return this;
        }

        public AwtrixAppMessage SetBackground(string value)
        {
            this["background"] = value;
            return this;
        }


        public AwtrixAppMessage SetRainbow(bool value = true)
        {
            return Set("rainbow", value);
        }

        public AwtrixAppMessage SetIcon(string value)
        {
            this["icon"] = value;
            return this;
        }

        public AwtrixAppMessage SetPushIcon(int value) => SetInt("pushIcon", value);

        public AwtrixAppMessage SetDuration(int value)
        {
            SetDuration(TimeSpan.FromSeconds(value));
            return this;
        }

        public AwtrixAppMessage SetDuration(TimeSpan value) => SetInt("duration", Convert.ToInt32(value.TotalSeconds));

        public AwtrixAppMessage SetLine(int[] value)
        {
            this["line"] = JoinInts(value);
            return this;
        }

        public AwtrixAppMessage SetLifetime(int value) => SetInt("lifetime", value);

        public AwtrixAppMessage SetLifetimeMode(int value) => SetInt("lifetimeMode", value);

        public AwtrixAppMessage SetBar(int[] value)
        {
            this["bar"] = JoinInts(value);
            return this;
        }

        public AwtrixAppMessage SetAutoscale(bool value) => Set("autoscale", value);

        public AwtrixAppMessage SetOverlay(string value)
        {
            this["overlay"] = value;
            return this;
        }

        public AwtrixAppMessage SetProgress(int value) => SetInt("progress", value);

        public AwtrixAppMessage SetProgressC(int[] value)
        {
            this["progressC"] = JoinInts(value);
            return this;
        }

        public AwtrixAppMessage SetProgressBC(int[] value)
        {
            this["progressBC"] = JoinInts(value);
            return this;
        }

        public AwtrixAppMessage SetScrollSpeed(int value) => SetInt("scrollSpeed", value);

        public AwtrixAppMessage SetEffect(string value)
        {
            this["effect"] = value;
            return this;
        }

        public AwtrixAppMessage SetEffectSpeed(int value) => SetInt("effectSpeed", value);

        public AwtrixAppMessage SetEffectPalette(string value)
        {
            this["effectPalette"] = value;
            return this;
        }

        public AwtrixAppMessage SetEffectBlend(bool value) => Set("effectBlend", value);


        private AwtrixAppMessage Set(string key, bool value)
        {
            this[key] = value.ToString().ToLower();
            return this;
        }

        private AwtrixAppMessage SetInt(string key, int value)
        {
            this[key] = value.ToString(CultureInfo.InvariantCulture);
            return this;
        }

        private static string JoinInts(int[] values)
        {
            return string.Join(',', values.Select(v => v.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Parse "1,2,3" (whitespace around items allowed) into integers using the invariant culture.
        /// </summary>
        internal static bool TryParseIntArray(string? value, out int[] result)
        {
            result = Array.Empty<int>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var parts = value.Split(',', StringSplitOptions.TrimEntries);
            var parsed = new int[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out parsed[i]))
                {
                    return false;
                }
            }

            result = parsed;
            return true;
        }

        /// <summary>
        /// Parse "255,0,0;0,255,0" into rows of integers using the invariant culture.
        /// </summary>
        internal static bool TryParseIntMatrix(string? value, out int[][] result)
        {
            result = Array.Empty<int[]>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var rows = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (rows.Length == 0)
            {
                return false;
            }

            var parsed = new int[rows.Length][];
            for (var i = 0; i < rows.Length; i++)
            {
                if (!TryParseIntArray(rows[i], out parsed[i]))
                {
                    return false;
                }
            }

            result = parsed;
            return true;
        }

        public override string ToString()
        {
            return string.Join(
                "; ",
                this.OrderBy(kvp => kvp.Key == "Text" ? "" : kvp.Key)       // always name first
                    .Select(kvp => $"{kvp.Key}={kvp.Value}")
            );
        }

        public string ToJson()
        {
            var dictionaryToSerialize = new Dictionary<string, object>(this.Count);

            foreach (var kvp in this)
            {
                dictionaryToSerialize[kvp.Key] = ToJsonValue(kvp.Key, kvp.Value);
            }

            return JsonSerializer.Serialize(dictionaryToSerialize);
        }

        private static object ToJsonValue(string key, string value)
        {
            // Text starting with "[" is an encoded JSON array of coloured text fragments
            if (key == TextKey && value != null && value.StartsWith("["))
            {
                try
                {
                    return JsonSerializer.Deserialize<JsonElement>(value);
                }
                catch
                {
                    return value;
                }
            }

            if (IntArrayKeys.Contains(key) && TryParseIntArray(value, out var array))
            {
                return array;
            }

            if (key == GradientKey && TryParseIntMatrix(value, out var matrix))
            {
                return matrix;
            }

            return value;
        }
    }
}
```

`NumberStyles.AllowLeadingSign` rejects `"1.5"`, `"1,000"` and embedded whitespace. Items are trimmed first, so surrounding whitespace is fine.

The file keeps the original's nullable-oblivious `string Get(...)`/`return null;`. The api project has `<Nullable>enable</Nullable>`, but those lines already produced warnings before this change and the build does not treat warnings as errors.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Domain"`

Expected: PASS. This includes the unchanged `AwtrixAppMessageBuilderTests` and `AwtrixAppMessageTest`; stored strings and text JSON do not change.

Then run: `dotnet test`

Expected: 0 failed. `AwtrixPublisherTests` compares against `message.ToJson()` itself, so it is unaffected.

- [ ] **Step 5: Commit**

```bash
git add src/api/Domain/AwtrixAppMessage.cs test/Test/Domain/AwtrixAppMessageJsonTests.cs
git commit -m "fix(message): emit line/bar/progress colours/gradient as JSON arrays; invariant-culture numbers (CR-34)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 6: ValueMap setter table and one-time load warnings (CR-34, ValueMap side)

**Files:**
- Create: `src/api/Apps/Configs/ValueMapSetters.cs`
- Modify: `src/api/Apps/Configs/ValueMap.cs` (whole file)
- Modify: `src/api/Apps/Configs/AppConfig.cs` (add one method after `FindMatchingValueMap`; add one `using`)
- Modify: `src/api/Apps/AwtrixApp.cs` (one line at the end of the constructor)
- Test: `test/Test/Configs/ValueMapSetterTests.cs` (new). Do **not** touch `ValueMapsTests.cs`.

**Interfaces:**
- Consumes (Task 5): `AwtrixAppMessage.TryParseIntArray(string?, out int[])`, `AwtrixAppMessage.TryParseIntMatrix(string?, out int[][])`
- Produces:
  - `internal static class ValueMapSetters` with:
    - `IReadOnlyCollection<string> Keys`
    - `bool IsKnown(string key)`
    - `bool TryApply(AwtrixAppMessage message, string key, string value)`
    - `bool IsValidValue(string key, string value)`
  - `public const string ValueMap.MatcherKey = "ValueMatcher"`
  - `public IReadOnlyList<string> ValueMap.GetConfigurationProblems()`
  - `public void AppConfig.LogValueMapProblems(ILogger? logger, string? device)`
  - `ValueMap.IsMatch`, `Clone` and `ToString` are unchanged. `Decorate(AwtrixAppMessage, ILogger)` keeps its signature.

- [ ] **Step 1: Write the failing tests**

Create `test/Test/Configs/ValueMapSetterTests.cs`:

```csharp
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.SlackStatus;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Globalization;
using System.Reflection;

namespace Test.Configs
{
    /// <summary>
    /// CR-34: every AwtrixAppMessage setter is reachable from a ValueMap; problems are logged once at load.
    /// </summary>
    public class ValueMapSetterTests
    {
        private readonly Mock<ILogger> _logger = new();

        private static int WarningCount(Mock<ILogger> logger) =>
            logger.Invocations.Count(i => i.Method.Name == nameof(ILogger.Log) && (LogLevel)i.Arguments[0] == LogLevel.Warning);

        // ---------- Setter table ----------

        [Fact]
        public void SetterTable_CoversEveryPublicSingleArgumentSetter()
        {
            var setterNames = typeof(AwtrixAppMessage)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name.StartsWith("Set", StringComparison.Ordinal) && m.GetParameters().Length == 1)
                .Select(m => m.Name.Substring(3))
                .Distinct()
                .ToList();

            Assert.Equal(30, setterNames.Count);
            foreach (var name in setterNames)
            {
                Assert.True(ValueMapSetters.IsKnown(name), $"ValueMap key '{name}' has no setter table entry");
            }
        }

        [Fact]
        public void Decorate_DoubleAndArraySetters_AreApplied()
        {
            var map = new ValueMap
            {
                { "ValueMatcher", "busy" },
                { "BlinkText", "0.5" },
                { "FadeText", "1.5" },
                { "Gradient", "255,0,0;0,255,0" },
                { "Bar", "1,2" },
                { "ProgressC", "255,0,0" },
            };
            var message = new AwtrixAppMessage();

            map.Decorate(message, _logger.Object);

            Assert.Equal("0.5", message["blinkText"]);
            Assert.Equal("1.5", message["fadeText"]);
            Assert.Equal("255,0,0;0,255,0", message["gradient"]);
            Assert.Equal("1,2", message["bar"]);
            Assert.Equal("255,0,0", message["progressC"]);
            Assert.Equal("{\"blinkText\":\"0.5\",\"fadeText\":\"1.5\",\"gradient\":[[255,0,0],[0,255,0]],\"bar\":[1,2],\"progressC\":[255,0,0]}", message.ToJson());
        }

        [Fact]
        public void Decorate_UnderCommaDecimalCulture_ParsesDoublesInvariantly()
        {
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                var message = new AwtrixAppMessage();

                new ValueMap { { "BlinkText", "0.5" } }.Decorate(message, _logger.Object);

                Assert.Equal("0.5", message["blinkText"]);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void Decorate_KeysAreCaseInsensitive()
        {
            var message = new AwtrixAppMessage();

            new ValueMap { { "ICON", "42" }, { "effectspeed", "7" } }.Decorate(message, _logger.Object);

            Assert.Equal("42", message["icon"]);
            Assert.Equal("7", message["effectSpeed"]);
        }

        [Fact]
        public void Decorate_ValueMatcherKeyInAnyCase_IsSkipped()
        {
            var message = new AwtrixAppMessage();

            new ValueMap { { "valueMatcher", "busy" } }.Decorate(message, _logger.Object);

            Assert.Empty(message);
        }

        [Fact]
        public void Decorate_InvalidValue_IsNotAppliedAndDoesNotWarn()
        {
            var message = new AwtrixAppMessage();

            var ex = Record.Exception(() => new ValueMap { { "BlinkText", "fast" }, { "Bar", "1,x" } }.Decorate(message, _logger.Object));

            Assert.Null(ex);
            Assert.Empty(message);
            Assert.Equal(0, WarningCount(_logger));
        }

        // ---------- GetConfigurationProblems ----------

        [Fact]
        public void GetConfigurationProblems_ShippedAppSettingsMaps_HaveNoProblems()
        {
            var shipped = new[]
            {
                new ValueMap { { "ValueMatcher", "" }, { "Icon", "1667" }, { "Text", "Go now!" }, { "Color", "#FFFFFF" } },
                new ValueMap { { "ValueMatcher", "^-" }, { "Icon", "52465" }, { "Color", "#FF0000" } },
                new ValueMap { { "ValueMatcher", "^(?!-).*" }, { "Icon", "52464" }, { "Color", "#FFDE21" } },
                new ValueMap { { "ValueMatcher", "busy" }, { "Icon", "38789" }, { "Text", "Busy" }, { "Color", "#FF0000" }, { "Background", "#FFFFFF" }, { "Duration", "60" } },
            };

            Assert.All(shipped, map => Assert.Empty(map.GetConfigurationProblems()));
        }

        [Fact]
        public void GetConfigurationProblems_UnknownKey_IsReported()
        {
            var problems = new ValueMap { { "ValueMatcher", "busy" }, { "Colour", "#FF0000" } }.GetConfigurationProblems();

            Assert.Contains(problems, p => p.Contains("Colour"));
        }

        [Fact]
        public void GetConfigurationProblems_InvalidValue_IsReported()
        {
            var problems = new ValueMap { { "ValueMatcher", "busy" }, { "Duration", "a minute" } }.GetConfigurationProblems();

            Assert.Contains(problems, p => p.Contains("Duration") && p.Contains("a minute"));
        }

        [Fact]
        public void GetConfigurationProblems_InvalidRegex_IsReported_AndIsMatchStillFallsBackToSubstring()
        {
            var map = new ValueMap { { "ValueMatcher", "[this is not a valid regex" } };

            Assert.Contains(map.GetConfigurationProblems(), p => p.Contains("regular expression"));
            Assert.True(map.IsMatch("meeting with [this is not a valid regex"));
            Assert.False(map.IsMatch("available"));
        }

        // ---------- Logged once at load ----------

        [Fact]
        public void LogValueMapProblems_LogsOneWarningPerProblem()
        {
            var config = new AppConfig { Type = "MqttRenderApp" };
            config.ValueMaps = new List<ValueMap>
            {
                new() { { "ValueMatcher", "ok" }, { "Colour", "#FF0000" } },
                new() { { "ValueMatcher", "[bad" }, { "Duration", "soon" } },
            };

            config.LogValueMapProblems(_logger.Object, "awtrix/clock1");

            Assert.Equal(3, WarningCount(_logger));
        }

        [Fact]
        public void AppConstruction_WithBadValueMap_WarnsOnce_AndDecorateDoesNotWarnAgain()
        {
            var config = new SlackStatusAppConfig { Type = "SlackStatusApp" };
            config.ValueMaps = new List<ValueMap> { new() { { "ValueMatcher", "busy" }, { "Colour", "#FF0000" } } };

            _ = new SlackStatusApp(_logger.Object, config, new AwtrixAddress { BaseTopic = "awtrix/clock1" },
                new Mock<IAwtrixService>().Object, new Mock<ISlackConnector>().Object);

            Assert.Equal(1, WarningCount(_logger));

            for (var i = 0; i < 3; i++)
            {
                config.ValueMaps[0].Decorate(new AwtrixAppMessage(), _logger.Object);
            }

            Assert.Equal(1, WarningCount(_logger));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Configs.ValueMapSetterTests"`

Expected: build FAILS with CS0103/CS1061. `ValueMapSetters`, `GetConfigurationProblems` and `LogValueMapProblems` do not exist yet.

- [ ] **Step 3: Implement**

Create `src/api/Apps/Configs/ValueMapSetters.cs`:

```csharp
using AwtrixSharpWeb.Domain;
using System.Globalization;

namespace AwtrixSharpWeb.Apps.Configs
{
    /// <summary>
    /// Static map from ValueMap key (case-insensitive, the setter name without "Set") to the
    /// <see cref="AwtrixAppMessage"/> setter it drives. Values are parsed with the invariant culture.
    /// A test asserts every public single-argument Set* method has an entry.
    /// </summary>
    internal static class ValueMapSetters
    {
        private static readonly Dictionary<string, Func<AwtrixAppMessage, string, bool>> Setters =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Text"] = Str((m, v) => m.SetText(v)),
                ["TextCase"] = Int((m, v) => m.SetTextCase(v)),
                ["TopText"] = Bool((m, v) => m.SetTopText(v)),
                ["Hold"] = Bool((m, v) => m.SetHold(v)),
                ["Stack"] = Bool((m, v) => m.SetStack(v)),
                ["TextOffset"] = Int((m, v) => m.SetTextOffset(v)),
                ["Center"] = Bool((m, v) => m.SetCenter(v)),
                ["Color"] = Str((m, v) => m.SetColor(v)),
                ["Gradient"] = IntMatrix((m, v) => m.SetGradient(v)),
                ["BlinkText"] = Dbl((m, v) => m.SetBlinkText(v)),
                ["FadeText"] = Dbl((m, v) => m.SetFadeText(v)),
                ["Background"] = Str((m, v) => m.SetBackground(v)),
                ["Rainbow"] = Bool((m, v) => m.SetRainbow(v)),
                ["Icon"] = Str((m, v) => m.SetIcon(v)),
                ["PushIcon"] = Int((m, v) => m.SetPushIcon(v)),
                ["Duration"] = Int((m, v) => m.SetDuration(v)),
                ["Line"] = IntArray((m, v) => m.SetLine(v)),
                ["Lifetime"] = Int((m, v) => m.SetLifetime(v)),
                ["LifetimeMode"] = Int((m, v) => m.SetLifetimeMode(v)),
                ["Bar"] = IntArray((m, v) => m.SetBar(v)),
                ["Autoscale"] = Bool((m, v) => m.SetAutoscale(v)),
                ["Overlay"] = Str((m, v) => m.SetOverlay(v)),
                ["Progress"] = Int((m, v) => m.SetProgress(v)),
                ["ProgressC"] = IntArray((m, v) => m.SetProgressC(v)),
                ["ProgressBC"] = IntArray((m, v) => m.SetProgressBC(v)),
                ["ScrollSpeed"] = Int((m, v) => m.SetScrollSpeed(v)),
                ["Effect"] = Str((m, v) => m.SetEffect(v)),
                ["EffectSpeed"] = Int((m, v) => m.SetEffectSpeed(v)),
                ["EffectPalette"] = Str((m, v) => m.SetEffectPalette(v)),
                ["EffectBlend"] = Bool((m, v) => m.SetEffectBlend(v)),
            };

        public static IReadOnlyCollection<string> Keys => Setters.Keys;

        public static bool IsKnown(string key) => key != null && Setters.ContainsKey(key);

        /// <summary>Apply <paramref name="value"/> via the setter for <paramref name="key"/>; false if unknown or unparsable.</summary>
        public static bool TryApply(AwtrixAppMessage message, string key, string value)
        {
            return key != null && Setters.TryGetValue(key, out var setter) && setter(message, value);
        }

        public static bool IsValidValue(string key, string value) => TryApply(new AwtrixAppMessage(), key, value);

        private static Func<AwtrixAppMessage, string, bool> Str(Action<AwtrixAppMessage, string> set) =>
            (message, value) =>
            {
                if (value is null)
                {
                    return false;
                }
                set(message, value);
                return true;
            };

        private static Func<AwtrixAppMessage, string, bool> Int(Action<AwtrixAppMessage, int> set) =>
            (message, value) =>
            {
                if (!int.TryParse(value?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }
                set(message, parsed);
                return true;
            };

        private static Func<AwtrixAppMessage, string, bool> Bool(Action<AwtrixAppMessage, bool> set) =>
            (message, value) =>
            {
                if (!bool.TryParse(value?.Trim(), out var parsed))
                {
                    return false;
                }
                set(message, parsed);
                return true;
            };

        private static Func<AwtrixAppMessage, string, bool> Dbl(Action<AwtrixAppMessage, double> set) =>
            (message, value) =>
            {
                if (!double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }
                set(message, parsed);
                return true;
            };

        private static Func<AwtrixAppMessage, string, bool> IntArray(Action<AwtrixAppMessage, int[]> set) =>
            (message, value) =>
            {
                if (!AwtrixAppMessage.TryParseIntArray(value, out var parsed))
                {
                    return false;
                }
                set(message, parsed);
                return true;
            };

        private static Func<AwtrixAppMessage, string, bool> IntMatrix(Action<AwtrixAppMessage, int[][]> set) =>
            (message, value) =>
            {
                if (!AwtrixAppMessage.TryParseIntMatrix(value, out var parsed))
                {
                    return false;
                }
                set(message, parsed);
                return true;
            };
    }
}
```

Replace the whole of `src/api/Apps/Configs/ValueMap.cs`:

```csharp
using AwtrixSharpWeb.Domain;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace AwtrixSharpWeb.Apps.Configs
{
    public class ValueMap : Dictionary<string, string>
    {
        public const string MatcherKey = "ValueMatcher";

        public string ValueMatcher
        {
            get => this.TryGetValue(MatcherKey, out var value) ? value : string.Empty;
            set => this[MatcherKey] = value;
        }

        public ValueMap Clone()
        {
            var clone = new ValueMap();
            foreach (var key in this.Keys)
            {
                clone.Add(key, this[key]);
            }
            return clone;
        }

        public bool IsMatch(string input)
        {
            if (string.IsNullOrEmpty(ValueMatcher) || string.IsNullOrEmpty(input))
                return false;

            try
            {
                return Regex.IsMatch(input, ValueMatcher, RegexOptions.IgnoreCase);
            }
            catch
            {
                // Fallback to string comparison if regex is invalid (reported once at load by GetConfigurationProblems)
                return input.Contains(ValueMatcher, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Apply every mapped key to <paramref name="message"/> via the static setter table.
        /// Unknown keys and invalid values are skipped; they were already reported at Warning when the app
        /// was constructed, so only Debug is logged here (Decorate can run every second).
        /// </summary>
        public void Decorate(AwtrixAppMessage message, ILogger logger)
        {
            foreach (var (key, value) in this)
            {
                if (IsMatcherKey(key))
                {
                    continue;
                }

                if (!ValueMapSetters.TryApply(message, key, value))
                {
                    logger?.LogDebug("ValueMap key '{Key}' with value '{Value}' not applied (unknown key or invalid value)", key, value);
                }
            }
        }

        /// <summary>
        /// Human-readable configuration problems: invalid non-empty regex, unknown keys, invalid values.
        /// An empty ValueMatcher is valid (it never matches, but maps may be used positionally).
        /// </summary>
        public IReadOnlyList<string> GetConfigurationProblems()
        {
            var problems = new List<string>();

            if (!string.IsNullOrEmpty(ValueMatcher))
            {
                try
                {
                    _ = new Regex(ValueMatcher);
                }
                catch (ArgumentException ex)
                {
                    problems.Add($"ValueMatcher '{ValueMatcher}' is not a valid regular expression ({ex.Message}); falling back to a case-insensitive substring match");
                }
            }

            foreach (var (key, value) in this)
            {
                if (IsMatcherKey(key))
                {
                    continue;
                }

                if (!ValueMapSetters.IsKnown(key))
                {
                    problems.Add($"Unknown key '{key}' is ignored");
                }
                else if (!ValueMapSetters.IsValidValue(key, value))
                {
                    problems.Add($"Value '{value}' for key '{key}' is invalid and is ignored");
                }
            }

            return problems;
        }

        public override string ToString()
        {
            return $"ValueMatcher={ValueMatcher}";
        }

        private static bool IsMatcherKey(string key) => string.Equals(key, MatcherKey, StringComparison.OrdinalIgnoreCase);
    }
}
```

In `src/api/Apps/Configs/AppConfig.cs`:
1. Add `using Microsoft.Extensions.Logging;` after `using System.Text.Json.Serialization;`.
2. Insert this method directly after the `FindMatchingValueMap` method (which ends `return _valueMaps?.FirstOrDefault(map => map.IsMatch(input));` then `}`):

```csharp
        /// <summary>
        /// Log every ValueMap configuration problem once, at Warning, naming the app type, device and map index.
        /// </summary>
        public void LogValueMapProblems(ILogger? logger, string? device)
        {
            if (logger == null || _valueMaps == null)
            {
                return;
            }

            for (var index = 0; index < _valueMaps.Count; index++)
            {
                var map = _valueMaps[index];
                if (map == null)
                {
                    continue;
                }

                foreach (var problem in map.GetConfigurationProblems())
                {
                    logger.LogWarning("{AppType} on {Device}: ValueMaps[{Index}]: {Problem}", Type, device, index, problem);
                }
            }
        }
```

In `src/api/Apps/AwtrixApp.cs`, find the constructor body. Re-read it first, because WS3 may have edited this file.

```csharp
            AwtrixAddress = awtrixAddress;
            AwtrixService = awtrixService;
            Config = config;
            Logger = logger;
```

Add this line immediately after `Logger = logger;`, as the last statement of the constructor:

```csharp
            config?.LogValueMapProblems(logger, awtrixAddress?.BaseTopic);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Configs"`

Expected: PASS. This includes the new `ValueMapSetterTests` and the unchanged `ValueMapDecorateTests`, `AppConfigConvertValueTests`, `AppConfigTests` and `AppConfigKeysTests`.

Then run: `dotnet test`

Expected: 0 failed. The TripTimer and MqttRender app tests still pass: `Decorate`'s behaviour for text, icon, color, duration and center is unchanged.

Then run: `grep -n "GetMethods\|Invoke(" src/api/Apps/Configs/ValueMap.cs`

Expected: no output (the reflection code is gone).

- [ ] **Step 5: Commit**

```bash
git add src/api/Apps/Configs/ValueMapSetters.cs src/api/Apps/Configs/ValueMap.cs src/api/Apps/Configs/AppConfig.cs src/api/Apps/AwtrixApp.cs test/Test/Configs/ValueMapSetterTests.cs
git commit -m "fix(valuemap): static setter table for all message setters; warn once at load on bad keys, values or regex (CR-34)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

## Final verification

- [ ] **Step 1: Full test run**

Run: `dotnet test`

Expected: 0 failed.

- [ ] **Step 2: Scope and regression checks**

Run each command. Expected result is shown after the arrow.
- `git diff --stat $WS5_BASE..HEAD` → only the files in the File map changed.
- `git diff $WS5_BASE..HEAD -- src/api/appsettings.json src/api/HostedServices src/api/Program.cs src/api/Apps/Buttons/ButtonApp.cs src/api/Apps/ScheduledApp.cs test/Test/Configs/ValueMapsTests.cs` → empty.
- `grep -rn "DateTime.Now\|DateTime.Today" src/api/Apps/Diurnal src/api/Apps/Buttons` → no output.
- `grep -rn "\.Result\b\|\.Wait()" src/api/Apps/Diurnal src/api/Apps/SlackStatus` → no output.

## Spec coverage (self-review)

| Spec item | Task |
|---|---|
| D1 eager Diurnal validation; CR-21 criteria 1-5 | 2 (schedule), 3 (app-level no-throw / no-publish) |
| CR-21 criterion 6: parse once, not on the tick path | 3 (`Initialize` parses; tick uses `DueBetween` only) |
| D2 startup rotation, one merged `Set`; CR-20 criteria 1, 2, 5 | 2, 3 |
| D3 (last, now] window, midnight, backwards clock, gap of 24 h or more; CR-20 criteria 3, 4, 6, 7, 8 | 2, 3 |
| D4 startup via `FireAndLog` | 3 |
| CR-20 criterion 9: no `DateTime.Now` | 3 Step 4, Final Step 2 |
| D5 Slack; CR-24 criteria 1-7 | 4 |
| D6 monotonic detector; CR-32 criteria 1-5 | 1 |
| D7 JSON arrays and invariant culture; CR-34 criteria 3, 4 | 5 |
| D8 setter table, `GetConfigurationProblems`, load-time warnings; CR-34 criteria 1, 2, 5, 6, 7 | 6 |
| CR-34 criterion 8: existing tests unchanged | 5 Step 4, 6 Step 4 |
| WS1-WS4 interface assumptions (spec section 4) | 0 |
