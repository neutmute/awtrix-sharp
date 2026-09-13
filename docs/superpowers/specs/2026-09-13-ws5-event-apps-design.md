# WS5 Event-Driven Apps and Display Correctness — Design

- **Date:** 2026-09-13
- **Branch:** `feature/redo`
- **Source:** `code-review.md` → "WS5. Event-driven apps and display correctness (Diurnal, Slack, Button, ValueMap)"
- **Findings:** CR-20, CR-21, CR-24, CR-32, CR-34
- **Plan:** `docs/superpowers/plans/2026-09-13-ws5-event-apps.md`
- **Status:** Approved for implementation. The owner was unavailable, so the planner made the decisions below and recorded them for review.
- **Execution order:** WS1 (done) → WS2 → WS3 → WS4 → **WS5**. WS5 relies only on WS1 contracts and the WS3 `InitAsync` rename. It does not use WS4 internals.

---

## 1. Goals

1. **Diurnal always shows the right setting** (CR-20):
   - At startup, it restores the settings in effect now, including entries carried over from yesterday evening.
   - At runtime, every entry in the window (last processed minute, current minute] is applied, so a stall, a coalesced tick, host suspend or the DST spring-forward gap never skips a setting.
2. **Diurnal config is validated eagerly** (CR-21):
   - Time keys and setting values are parsed once, at init, with the invariant culture.
   - Bad entries are logged and skipped.
   - Nothing that reaches the tick path can throw a parse error.
3. **SlackStatusApp is null-safe and async** (CR-24):
   - With no Slack user id configured it warns once and does not subscribe.
   - A null or empty status clears the app.
   - The handler never blocks the SlackNet dispatch thread, and it never throws.
4. **Double-click detection uses monotonic time** (CR-32). Wall-clock jumps (DST end, NTP step) cannot create a double-click. Click and DoubleClick semantics are unchanged.
5. **ValueMap drives every message setter correctly** (CR-34):
   - A static setter table covers all 30 single-argument `AwtrixAppMessage` setters, including the double and array ones.
   - `line`, `bar`, `progressC`, `progressBC` and `gradient` are emitted as JSON arrays.
   - Numbers are formatted with the invariant culture.
   - Unknown keys, invalid values and invalid regexes are logged once, when the app is constructed.

## 2. Non-goals

| Not in WS5 | Owner / reason |
|---|---|
| Case-insensitive `ValueMap`/`AppConfigKeys` dictionaries, including the `ValueMatcher` property getter (CR-22) | WS7 |
| Reading `AWTRIXSHARP_SLACK__USERID` through `IConfiguration` instead of `Environment` (CR-14) | WS7. WS5 keeps `Config.Get("SlackUserId", "AWTRIXSHARP_SLACK__USERID")`. |
| `SlackConnector.StopAsync` NRE (CR-16), static client fields (CR-41) | WS3 / WS8. `SlackConnector.cs` is not edited. |
| `ButtonApp` `.Wait()` on subscribe, ButtonApp logger category | WS3 / WS8. `ButtonApp.cs` is not edited. |
| Click and DoubleClick mutually exclusive | Deferred owner decision. Semantics must not change. |
| Restoring the commented-out `test/Test/Configs/ValueMapsTests.cs` | WS4 lists it. WS5 does not edit that file, so the two workstreams cannot conflict; WS5 tests go in new files. |
| Emitting scalar values (bool/int) as typed JSON instead of strings | CR-34 names only arrays and culture. Today's device behaviour with string scalars is known to work, so changing it is unrequested risk. |
| Accepting hex strings for `progressC`/`progressBC`/`gradient` ValueMap values, or RGB arrays for `color` | Not in CR-34. `color` stays a raw string, as today. |
| Configurable Diurnal timezone | Deferred. Host-local time stays the source. |
| `AwtrixApp.Init`/`Dispose` shape, per-app try/catch in Conductor | WS3 |
| Rate-limiting repeated warnings | Deferred (WS1 risk) |

## 3. Constraints

- **Config compatibility is mandatory.**
  - The Diurnal time-map format does not change: key `HHmm`, value `Name=Value[;Name=Value]`, setting names `Brightness` and `GlobalTextColor` (case-insensitive).
  - ValueMap keys and values keep working. Keys that were silently ignored before (`BlinkText`, `FadeText`, `Gradient`, array keys with fewer than 3 values) now take effect; CR-34 marks this as the intended fix.
  - An invalid `ValueMatcher` regex still falls back to a case-insensitive substring match; a working config may rely on that.
  - An empty `ValueMatcher` (used by the shipped TripTimer config) stays valid and is not warned about.
- **No new configuration**, required or optional.
- **Button Click/DoubleClick semantics are unchanged:**
  - The first press raises Click.
  - A second press within 300 ms (inclusive) raises DoubleClick instead of Click and resets the detector.
- **Transport payloads:**
  - MQTT topics and HTTP URLs do not change.
  - `AwtrixSettings` JSON does not change.
  - `AwtrixAppMessage` JSON changes only for the five array keys.
  - `AwtrixAppMessage` dictionary values (what `message["line"]` returns) do not change, so existing builder tests stay valid.
- **Dependencies:** target `net10.0`, no new packages. `Microsoft.Extensions.TimeProvider.Testing` 10.0.0 is already referenced by `test/Test`.
- **Out-of-scope files are not edited:** `SlackConnector.cs`, `ButtonApp.cs`, `Conductor.cs`, `Program.cs`, `ScheduledApp.cs`, `TimerService.cs`, `ValueMapsTests.cs`.
- **Tests:** unit tests only. No step runs the application or needs a broker, clock device or Slack.

## 4. Assumed interfaces from WS1-WS4

Verified by Task 0 of the plan. If any differ, the executor adapts names, not behaviour.

| Assumption | Source |
|---|---|
| `IAwtrixApp.InitAsync()` returns `Task`; `AwtrixApp<TConfig>` calls `protected abstract void Initialize()` (or an async equivalent) exactly once | WS3 |
| `AwtrixApp<TConfig>(ILogger logger, TConfig config, AwtrixAddress awtrixAddress, IAwtrixService awtrixService)` constructor | WS1 (current) |
| `protected Task FireAndLog(Func<Task> work, string operation)` on `AwtrixApp` | WS1 D4 |
| `protected Task<bool> Set(AwtrixSettings)`, `AppUpdate(AwtrixAppMessage)`, `AppClear()` on `AwtrixApp` | current |
| `IClock.Now` returns `DateTimeOffset` from `TimeProvider.GetLocalNow()` | WS1 D1 |
| `ClockTickEventArgs.Time` is local wall-clock time, truncated to the second, `Kind=Local`; ticks may be coalesced | WS1 D2 |
| `DiurnalApp(ILogger, IClock, ITimerService, AppConfig, AwtrixAddress, IAwtrixService)` | WS1 (current) |
| `SlackStatusApp(ILogger, SlackStatusAppConfig, AwtrixAddress, IAwtrixService, ISlackConnector)`; `ISlackConnector.UserStatusChanged` | WS1 D9 |
| `AwtrixSettings.ToString()` is safe on an empty dictionary | WS1 D10 (CR-21's ToString item is already done) |

## 5. Design decisions

### D1. `DiurnalSchedule`: a pure, eagerly validated time map (CR-21)
- **New file:** `src/api/Apps/Diurnal/DiurnalSchedule.cs`. It has no I/O and no clock.
- **Entry point:** `static DiurnalSchedule Parse(IReadOnlyDictionary<string,string>? config, ILogger logger)`.
- **Time keys:** parsed with `TimeSpan.TryParseExact(key.Trim(), "hhmm", CultureInfo.InvariantCulture, …)`, which is the same format as today. A key that fails is logged at Warning and skipped.
- **Values:**
  - Split on `;` (remove empties, trim), then each part on `=`. A part is used only when it yields exactly 2 non-empty pieces, as today.
  - `brightness` uses `byte.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, …)`, so `dim`, `300`, `-1` and `8.5` are rejected with a Warning.
  - `globaltextcolor` takes the raw string, as today.
  - Any other name logs a Warning and is skipped.
- **Empty entries:** an entry with no valid setting is dropped with a Warning.
- **Representation:** entries are stored as `DiurnalEntry(TimeSpan Time, AwtrixSettings Settings)`, sorted by time. `AwtrixSettings` already maps names to device keys (`BRI`, `TCOL`), so the tick path only merges dictionaries and cannot throw.

### D2. Startup state is a 24-hour rotation (CR-20)
- **Method:** `AwtrixSettings StateAt(DateTime localNow)` works at minute resolution.
- **Rotation order:** yesterday's entries with `Time > now`, then today's entries with `Time <= now`. Each is merged in chronological order, and the last value of each key wins.
- **One publish:** the result goes out as a single `Set`. The old replay sent one `Set` per entry. "Only the final value of each setting needs sending" (CR-20).
- **Boundary:** an entry exactly at the startup minute is included (`<=`). The runtime window starts after that minute, so the entry is applied exactly once.
- **Empty map:** if the schedule is empty, the app warns, does not subscribe and does not publish.
- **Why merge instead of re-issuing every entry:** it cuts the publish count, and it cannot flash intermediate states on the clock.

### D3. Runtime applies the window (last, now] (CR-20)
- **State:** `DiurnalApp` holds `_lastProcessed`, a local `DateTime` truncated to the minute and guarded by a lock.
  - It is set to the startup minute **before** subscribing to `MinuteChanged`.
  - Each tick sets `now = Truncate(e.Time)`, reads `from = _lastProcessed`, and stores `_lastProcessed = now`.
- **Method:** `AwtrixSettings DueBetween(DateTime afterExclusive, DateTime upToInclusive)`.
  - It checks every calendar day from `from.Date` to `to.Date`, so a window that crosses midnight works.
  - Occurrences in the window are merged chronologically.
  - A window of 24 hours or more returns `StateAt(to)`, the same result as a restart.
  - `to <= from` returns empty.
- **DST:**
  - *Spring-forward gap:* a tick jumping 01:59 → 03:00 applies a 02:30 entry.
  - *Fall-back:* the clock moving backwards (`now < from`) applies nothing, logs at Information and resets `_lastProcessed = now`. Entries in the repeated hour may re-apply once, which is idempotent and harmless (consistent with WS1 D2).
  - Rejected alternative: keeping `_lastProcessed` as a high-water mark. A large backwards NTP correction would freeze the schedule for that long.
- **Publishing:** a non-empty result goes to `Set` through `FireAndLog`. The whole tick body, including the computation, runs inside `FireAndLog`, so the handler never throws.

### D4. Startup publish uses `FireAndLog`, not `await`
- The startup `Set` in `Initialize()` runs as `_ = FireAndLog(() => Set(state), "DiurnalStartupRestore")`.
- **Why:** it works whether WS3 keeps `Initialize()` synchronous or makes it async, and a dead device cannot delay or fail init. That matches CR-05's goal.
- **Tests:** Moq's completed tasks make it run synchronously.

### D5. `SlackStatusApp` (CR-24)
- **No user id:**
  - `Initialize()` reads the id with `Config.Config.Get("SlackUserId", "AWTRIXSHARP_SLACK__USERID")`, unchanged.
  - If the id is null or whitespace, it logs one Warning naming both sources and returns **without subscribing**.
  - Otherwise it stores the trimmed id and subscribes.
- **User match:** the handler returns immediately unless `string.Equals(_trackingUserId, e.UserId, StringComparison.Ordinal)`. That comparison is null-safe, and a null `e` is also ignored.
- **Status handling:** matching events go to `FireAndLog(() => ShowStatusAsync(e), …)`. There is no `.Result`.
  - `string.IsNullOrEmpty(e.StatusText)` → `await AppClear()`. Today, text `""` with any emoji clears, and null text published `text=null`. Null now clears, the same as empty.
  - Otherwise the app tries a ValueMap on the status text, then on the emoji (null-safe). With no match it shows the text for 50 s, as today.
- **Logging:** interpolated log calls become structured templates.

### D6. `DoubleClickDetector` over `TimeProvider` timestamps (CR-32)
- **Constructor:** `DoubleClickDetector(double thresholdMilliseconds = 300, TimeProvider? timeProvider = null)`, defaulting to `TimeProvider.System`.
- **Mechanism:** it stores `long? _lastClickTimestamp` from `GetTimestamp()` and compares `GetElapsedTime(last, now) <= threshold`. Elapsed time is monotonic and never negative.
- **Semantics:** the same as before:
  - A first click, or a click after a double, returns false.
  - Within the threshold (inclusive) it returns true and resets.
  - A negative threshold never reports a double-click.
- **`ButtonState`:** gets an optional `TimeProvider? timeProvider = null` constructor parameter that passes through to the detector. `ButtonApp` is not changed and keeps using the system provider.

### D7. AwtrixAppMessage: JSON arrays and invariant culture (CR-34)
- **Storage does not change.** Values stay strings in the dictionary: `line`/`bar`/`progressC`/`progressBC` as `"1,2,3"` and gradient as `"255,0,0;0,255,0"`. `ToString()` and existing builder tests are unaffected.
- **`ToJson()`:**
  - `line`, `bar`, `progressC` and `progressBC` whose value parses as a comma-separated integer list are written as `int[]`.
  - `gradient` whose value parses as `;`-separated integer lists is written as `int[][]`.
  - A value that does not parse (for example, a raw string set through the indexer) is still written as a string.
  - The existing `text` JSON-array handling is kept.
- **Culture:** every numeric setter uses `CultureInfo.InvariantCulture`: the int setters, `SetDuration`, array joins, and especially `SetBlinkText`/`SetFadeText`, where `double` formatting is where culture actually matters.
- **Shared parsers:** `internal static bool TryParseIntArray(string?, out int[])` and `TryParseIntMatrix(string?, out int[][])` live on `AwtrixAppMessage` and are reused by the setter table.

### D8. ValueMap: static setter table and one-time validation (CR-34)
- **Setter table:** new `internal static class ValueMapSetters` (`src/api/Apps/Configs/ValueMapSetters.cs`) maps each key, case-insensitively, to a `TryApply(AwtrixAppMessage, string) → bool` delegate.
  - Parsers: string (non-null), `int.TryParse` invariant, `bool.TryParse`, `double.TryParse(NumberStyles.Float, invariant)`, int array (at least 1 element) and int matrix.
  - It covers all 30 public single-argument `Set*` methods. A reflection test (test-only) makes adding a setter without a table entry fail the build's tests.
  - `Duration` keeps the int-seconds overload.
- **`ValueMap.Decorate`:**
  - Skips the `ValueMatcher` key in any casing.
  - Applies each other key through the table.
  - Logs unknown keys and invalid values at **Debug** only, because TripTimer decorates every second and the warning was already logged at load.
  - The reflection code and `TryParseColorArray` are removed.
- **Validation:** `ValueMap.GetConfigurationProblems()` returns a list of human-readable problems:
  - an invalid non-empty regex (message says it falls back to a substring match)
  - an unknown key
  - an invalid value for a known key
- **Logging at load:** `AppConfig.LogValueMapProblems(ILogger, string? device)` logs each problem at Warning with the app type, device and map index. The `AwtrixApp` constructor calls it, so every app warns once at creation.
  - Rejected: logging from `Init`/`InitAsync`, which WS3 owns.
  - Rejected: lazy first-use logging, which is not "at load".
- **`IsMatch` is unchanged:** regex, case-insensitive, substring fallback on an invalid pattern.

## 6. Acceptance criteria

### CR-20: Diurnal replay ignores yesterday; a missed minute skips a setting
Criteria 1-3 use the shipped config: 0600 `Brightness=8`, 0700 `GlobalTextColor=#FFFFFF`, 1900 `GlobalTextColor=#FF0000`, 2100 `Brightness=1`.

1. **Restart at 03:00:** `InitAsync` publishes exactly one `Set` with `BRI=1` and `TCOL=#FF0000`. *(DiurnalAppTests, DiurnalScheduleTests)*
2. **Restart at 06:30:** exactly one `Set` with `BRI=8` and `TCOL=#FF0000`. *(DiurnalScheduleTests)*
3. **Stall from 20:59 to 21:01:** a single minute tick at 21:01 publishes `BRI=1` and nothing else. *(DiurnalAppTests, DiurnalScheduleTests)*
4. **Midnight:** a window from 23:59 to 00:01 the next day applies a `0000` entry. *(DiurnalAppTests, DiurnalScheduleTests)*
5. **Startup minute:** an entry at the startup minute is applied once at startup and not again by a tick in the same minute. *(DiurnalAppTests)*
6. **Clock moves backwards:** no publish on the backwards tick, and the next forward tick applies entries in (new time, tick]. *(DiurnalAppTests)*
7. **Gap of 24 h or more:** publishes the rotation state at the tick time. *(DiurnalAppTests, DiurnalScheduleTests)*
8. **Same key twice in a window:** the later entry wins. *(DiurnalScheduleTests)*
9. **No `DateTime.Now`/`DateTime.Today`** in `DiurnalApp.cs` or `DiurnalSchedule.cs` (code review, grep).

### CR-21: Diurnal setting values validated lazily
1. `Brightness=dim`, `Brightness=300` and `Brightness=-1` are skipped with a Warning at parse. Neither `InitAsync` nor a tick throws, and `Set` is never called. *(DiurnalScheduleTests, DiurnalAppTests)*
2. `Brightness=dim;GlobalTextColor=#00FF00` keeps the valid `TCOL` part. *(DiurnalScheduleTests, DiurnalAppTests)*
3. An entry with only unknown setting names is dropped, and `Set` is never called with empty settings. *(DiurnalScheduleTests, DiurnalAppTests)*
4. Invalid time keys (`2400`, `not-a-time`) are dropped with a Warning. *(DiurnalScheduleTests)*
5. Setting names are case-insensitive (`BRIGHTNESS=5`). *(DiurnalScheduleTests)*
6. Parsing happens once, in `Initialize`. The tick path contains no `Parse` calls (code review).

### CR-24: SlackStatusApp NRE with no user id
1. With `SlackUserId` empty or whitespace and the env var unset, `InitAsync` does not throw and never subscribes to `UserStatusChanged`. *(SlackStatusAppTests)*
2. An event for a different user, or with a null `UserId`, publishes nothing and does not throw. *(SlackStatusAppTests)*
3. For the tracked user, `StatusText` of `""` or `null` calls `AppClear` and never `AppUpdate`. *(SlackStatusAppTests)*
4. For the tracked user with text and no map, `AppUpdate` is called with `text=<status>` and `duration=50`. *(SlackStatusAppTests)*
5. A ValueMap matches the text first, then the emoji. A null emoji does not throw. *(SlackStatusAppTests)*
6. A faulting `AppUpdate` does not propagate out of the event raise. *(SlackStatusAppTests)*
7. `SlackStatusApp.cs` contains no `.Result`/`.Wait()` (code review).

### CR-32: DoubleClickDetector uses DateTime.Now
1. With `FakeTimeProvider`, a second click after exactly 300 ms is a double-click, and one after 301 ms is not. *(DoubleClickDetectorTests)*
2. A wall clock jumping back 1 hour while 5 s of monotonic time pass is not a double-click. *(DoubleClickDetectorTests)*
3. The existing detector, `ButtonState` and `ButtonApp` tests pass unchanged, including "press-release-press quickly → 1 Click + 1 DoubleClick". *(existing)*
4. `ButtonState` with a fake provider: press, release, 500 ms, press → 2 Clicks and 0 DoubleClicks. *(ButtonStateTests)*
5. No `DateTime.Now` in `src/api/Apps/Buttons` (grep).

### CR-34: ValueMap setters ignored; arrays as strings; invalid regex silent
1. ValueMap keys `BlinkText=0.5`, `FadeText=1.5`, `Gradient=255,0,0;0,255,0` and `Bar=1,2` are applied. *(ValueMapSetterTests)*
2. Every public single-argument `Set*` method on `AwtrixAppMessage` has a table entry. *(ValueMapSetterTests, reflection)*
3. `ToJson` emits `"line":[1,2,3]`, `"bar":[…]`, `"progressC":[…]`, `"progressBC":[…]` and `"gradient":[[255,0,0],[0,255,0]]`. An unparsable array value is still emitted as a string. *(AwtrixAppMessageJsonTests)*
4. Under `de-DE`, `SetBlinkText(0.5)` stores `"0.5"`. *(AwtrixAppMessageJsonTests)*
5. `GetConfigurationProblems()`:
   - reports an invalid regex, an unknown key and an invalid value;
   - reports nothing for the shipped appsettings maps, including the empty-matcher TripTimer map.
   *(ValueMapSetterTests)*
6. Constructing an app with a bad map logs exactly one Warning per problem. Calling `Decorate` repeatedly adds no further Warnings. *(ValueMapSetterTests)*
7. Invalid regex matching still falls back to substring (existing behaviour preserved). *(ValueMapSetterTests)*
8. The existing `ValueMapDecorateTests`, `AppConfigConvertValueTests`, `AwtrixAppMessageBuilderTests` and `AwtrixAppMessageTest` pass unchanged. *(existing)*

## 7. Testing strategy

- **TDD per task:**
  1. Write the failing test.
  2. Run it filtered with `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~…"` and confirm it fails.
  3. Implement.
  4. Re-run it filtered and confirm it passes.
  5. Run the full `dotnet test` with 0 failed before each commit.
- **Pure logic first:** `DiurnalSchedule` is tested directly with fixed `DateTime` values. No clock or mocks are needed apart from a mock logger for Warning checks.
- **Apps:**
  - Use `Mock<ITimerService>` (`Raise`), `Mock<ISlackConnector>` (`Raise`, `VerifyAdd`) and `Mock<IAwtrixService>` with a `Callback` that captures `AwtrixSettings`/`AwtrixAppMessage`.
  - Moq's completed tasks make `FireAndLog` synchronous, so a verify can follow the raise directly.
  - All dates are fixed (2026-09-13). No `DateTime.Today`: the existing Diurnal tests that used it are rewritten, because a date-dependent window would make them flaky.
- **Rewritten test files:**
  - `DiurnalAppTests.cs` is rewritten because the startup rotation (D2) changes the call counts the old tests asserted. Each old test's intent is kept under the same or a clearer name; the plan lists the mapping.
  - `SlackStatusAppTests.cs` is rewritten on mocks. The old file built real `MqttConnector`/`HttpPublisher` instances, which predate the WS1 interface seams.
- **Environment variable:** the "no user id" Slack test clears `AWTRIXSHARP_SLACK__USERID` for the test process and restores it in `finally`. Only `SlackStatusApp` reads it, and xUnit runs tests within a class serially.
- **Time:** use `FakeTimeProvider` and a small hand-written `TimeProvider` subclass (for the backwards wall clock) in the detector tests. No sleeps.
- **Culture:** set `CultureInfo.CurrentCulture` to `de-DE` in try/finally. It flows per async context, so parallel tests are unaffected.
- **Not run:** the application, broker, device or Slack.

## 8. Risks

- **WS3 signature drift.** WS3 may rename `Initialize` or change `Init`. Task 0 checks this and gives explicit adaptation rules. Only the method names in the tests and overrides change.
- **Visible behaviour changes that are the intended fixes:**
  - After a restart the clock immediately takes yesterday-evening or today settings where it previously kept stale ones.
  - ValueMap keys that were ignored now render, for example a `Gradient` someone left in config.
  - Array keys become JSON arrays. This was broken on the device before, so no working config can regress.
  - Include these in release notes.
- **Repeated DST hour.** Diurnal may re-send a setting once. This is idempotent.
- **Environment variable mutation in one test.** The value is restored in `finally`. If a future test class also reads the variable, move both into a shared xUnit collection.
- **Merge conflicts.** `AwtrixApp.cs` gets a one-line constructor addition. WS3 edits the same file (`Init`/`Dispose`), so Task 0 re-reads it before editing. `AppConfig.cs` may be edited by WS7 later (comparer), which is a different region.
