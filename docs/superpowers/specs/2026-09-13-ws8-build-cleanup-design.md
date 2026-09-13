# WS8: Build, CI and cleanup (design)

- **Source:** `code-review.md` § WS8 and findings CR-41, CR-42, CR-43, CR-44 and CR-45. CR-17 (the CI test job) is **already done** in `2a681ef` and is excluded.
- **Execution order:** WS4 → WS5 → WS6 → WS7 → **WS8**. WS8 runs last.
- **Written against:** the tree at `9bdf019` (WS1-WS4 implemented) plus these planned-but-unexecuted documents:
  - `docs/superpowers/plans/2026-09-13-ws5-event-apps.md`
  - `docs/superpowers/specs/2026-09-13-ws6-trip-planner-design.md` (the WS6 plan was not yet written)
  - `docs/superpowers/plans/2026-09-13-ws7-config-hardening.md`

  Every task therefore starts from a grep, not a line number. Plan Task 0 re-greps every target and drops anything WS5-WS7 already fixed.
- **Plan:** `docs/superpowers/plans/2026-09-13-ws8-build-cleanup.md`

## 1. Goals

1. **Container build (CR-45):**
   - The Docker restore layer copies both project files, so it caches properly.
   - One `dotnet publish --no-restore` replaces build-then-publish.
   - `EXPOSE 8081` is gone.
   - The image contract is unchanged: `ENTRYPOINT ["dotnet", "awtrix-api.dll"]`, `/app-api`, port 8080, and the `GIT_COMMIT` / `GIT_COMMIT_SHORT` build args.
2. **HTTP pipeline (CR-45):** remove `UseHttpsRedirection`. The service is HTTP only, so it did nothing except log a warning. Delete the unreferenced `Debug/SlackUserHarvester.cs`, which calls `Environment.Exit`.
3. **SlackConnector (CR-41):**
   - Client state moves to instance fields.
   - Dead members go: `_slackApiClient`, the `UserDnChanged` event, `SlackDndChangedEventArgs`, the commented-out API client, and unused usings including `Newtonsoft.Json.Linq`.
   - A doc comment says DND is not supported.
4. **Small correctness fixes (CR-42, CR-44):**
   - `AwtrixAppMessage.ToString` lists `text` first.
   - The unreachable `Quantize` branch is removed.
   - `TripSummary.ToString` shows total minutes, so 75 min reads "75 mins", not "15 mins".
5. **Dead or misleading code and logging (CR-43):** whatever is still present after WS4-WS7 (see §4).
6. **Deferred minors from the WS1-WS3 reviews:** see §5.

## 2. Non-goals

| Not in WS8 | Owner / reason |
|---|---|
| CR-17 CI test job | Done (`2a681ef`) |
| Everything in code-review.md "Deferred / needs owner decision" | Owner |
| Changing any log **level**, including WS6's per-departure Information lines in `SetDepartures` (WS6 spec says "WS8 can demote it") | Owner. The WS8 constraint is that structured-logging conversions keep their level. |
| The `ex.Message`-only warnings in `MqttConnector` (connect/publish/disconnect/subscribe) and `HttpPublisher` | Left as is. They are deliberate one-line transport warnings on a reconnect path, not listed in CR-43, and adding stack traces would flood logs while the broker is down. |
| A project-wide unused-`using` sweep (IDE0005 / `dotnet format`) | Not done. It needs `GenerateDocumentationFile` / `EnforceCodeStyleInBuild` and would touch almost every file while other agents commit. WS8 removes only the usings CR-41/CR-43 name, plus the unused usings in `SlackConnector.cs`, which WS8 edits anyway. |
| The readme row `AWTRIXSHARP_SLACK__BOTTOKEN` ("reserved for future use") | Left. It documents an env var, not the harvester. |
| Reconnecting Slack beyond SlackNet's own socket-mode reconnect | Documented only (CR-41 says "document") |
| `.github/workflows/docker-publish.yml` | Unchanged. It already runs tests and passes the same build args. |

## 3. Constraints

- `appsettings.json` and environment-variable configuration stay compatible. No key is added, renamed or removed. `SlackUserId` stays readable as an app `Config` dictionary key; only the never-populated typed property goes.
- Docker image contract: same `ENTRYPOINT`, `WORKDIR /app-api`, `EXPOSE 8080`, top-level `ARG GIT_COMMIT` / `ARG GIT_COMMIT_SHORT` redeclared in the publish stage, and `/p:GIT_COMMIT` / `/p:GIT_COMMIT_SHORT` passed to publish. Only `EXPOSE 8081` is dropped.
- Structured-logging conversions keep the original level.
- No step runs the app, connects to a broker or clock, or starts a container.
- Commits use explicit `git add <paths>` (or `git rm <path>` for deletions), never `-A`. Other agents commit in this tree.
- Tasks are small file-local edits located by grep, because WS5-WS7 edit the same files first (`Program.cs`, `SlackConnector.cs`, `Conductor.cs`, `AwtrixAppMessage.cs`, `TripTimerApp.cs`, `TripPlannerService.cs`, `TripPlannerController.cs`).

## 4. Finding-by-finding status and decisions

### CR-45 (build)

- **D1 Dockerfile:**
  - The `build` stage copies `src/api/awtrix-api.csproj` and `src/transportOpenData/TransportOpenData.csproj` into the matching folders, then restores, then `COPY src/ .`.
  - The `publish` stage runs one `dotnet publish -c $BUILD_CONFIGURATION --no-restore -o /app-api/publish /p:UseAppHost=false /p:GIT_COMMIT=... /p:GIT_COMMIT_SHORT=...`.
  - The `dotnet build` line is deleted.
  - Stage names `base`/`build`/`publish`/`final` are kept, because Visual Studio's container tooling uses `base`.
  - `.dockerignore` already excludes `**/bin` and `**/obj`, so `COPY src/ .` does not overwrite the restored `obj/project.assets.json`.
- **D2 Verification without Docker:** the plan simulates the two Docker layers in a temp directory:
  1. Restore with only the two csproj files present.
  2. Copy the sources without bin/obj.
  3. `dotnet publish --no-restore`, then check that `awtrix-api.dll` exists.

  It also runs grep checks for the image contract. `docker build` plus `docker image inspect` (not `docker run`) run only when `docker info` succeeds.
- **D3 `UseHttpsRedirection`:** delete the call wherever it lives after WS7 (WS7 moves it into `ConfigureHttpPipeline`). No test is added, because under TestHost with no HTTPS port the middleware does nothing observable. Verification is grep, build and the full test suite.
- **D4 launchSettings:** the Visual Studio-only "Container (Dockerfile)" profile drops `ASPNETCORE_HTTPS_PORTS: 8081` and sets `useSSL` to `false`, so VS no longer maps a port nothing listens on. The image is unaffected.
- **D5 SlackUserHarvester:** delete the file. It is unreferenced and was never behind a CLI flag. The Debug folder disappears with it.

### CR-41 (SlackConnector)

- **D6:**
  - `_slackSocketClient` becomes a nullable instance field, assigned through a local in `ExecuteAsync` so there is no nullable warning.
  - `_slackApiClient` and its commented-out builder are deleted.
  - `UserDnChanged` is deleted. It is not on `ISlackConnector` and is never raised.
  - `HostedServices/Slack/SlackDndChangedEventArgs.cs` is deleted; its only reference was that event.
  - `SlackUserEventArgs` stays, because `SlackUserStatusChangedEventArgs` derives from it.
  - Usings removed: `Newtonsoft.Json.Linq`, `System.Net.Http`, `System.Net.Http.Headers`, `System.Text` and `System.Text.Json`. The build is the check.
- **D7 Test:** a behavioural TDD test shows that stopping one `SlackConnector` does not disconnect another instance's socket client. With static fields it does. WS7's optional constructor parameter doesn't affect the test.

### CR-42 (misc defects)

- **ButtonApp logger category:** **already fixed**; `Conductor.cs` uses `CreateLogger<ButtonApp>()`. Dropped.
- **D8 `"Text"` vs `"text"` in `AwtrixAppMessage.ToString`:**
  - Still present, and WS5's rewritten `AwtrixAppMessage` keeps the `"Text"` literal.
  - Fix: compare against the `TextKey` constant WS5 introduces, or `"text"` if WS5 has not landed.
  - TDD: `ToString` starts with `text=`.
- **D9 Quantize:** delete `else if (p < 4) blink = 1;`. It is unreachable, because `n == 0` exactly when `p < 4`. The existing theory keeps guarding it; two rows are added (5→3, 50→47) that pass before and after.

### CR-43 (dead code, logging)

| Item | Status at `9bdf019` | WS8 action |
|---|---|---|
| `MqttRenderApp` unreachable `message.Text == null` check | Present | **D10** Delete it. The message is built with `SetText(payload)`, and ValueMap only sets `text` from a config string, never null (JSON null binds as `""`). Task 0 greps for a null-text path and keeps the check if WS5 added one. |
| `AwtrixApp.AppClear` `Config.Name == null` "Diurnal" guard | Present, **and covered by two tests** (`InitAsync_WithNullName_SkipsAppClear_ButStillInitializes`, `AppClear_WithNullConfigName_ReturnsFalseWithoutCallingService`) | **D11 Deviation from CR:** keep the guard. Without it, a nameless app would publish to `.../custom/`. Replace the misleading "Diurnal sending empty custom payload" comment with the real reason. |
| `SlackStatusAppConfig.SlackUserId` property | Present, never read (`As<T>` copies only `Config`, `Environment`, `Type` and `ValueMaps`) | **D12** Delete the property. The `"SlackUserId"` dictionary key used by WS5/WS7 is unaffected. |
| `TripTimerApp` inverted comment | Moved. `// Is an odd second` beside `clockText.Contains(":")`, but `FormatClockString` shows the colon on **even** seconds | Correct the comment |
| `TripTimerApp` unused `departuresCsv` | Gone (WS4) | Drop |
| `TripPlannerService` `using Newtonsoft.Json.Linq` | Present (WS6 may rewrite) | Delete if still present |
| `TripPlannerController` commented-out `D:\downloads` writer | Present. WS6 spec §2 hands it to WS8. | Delete |
| `ScheduledApp` "Error in TripTimerApp" and `LogError($"...{ex}", ex)` | Gone (WS4 rewrite; all templates structured) | Drop |
| `TripTimerApp` / `MqttRenderApp` logging `ex.Message` only | Gone (WS4 removed those catches) | Task 0 re-greps `src/api/Apps` and `src/api/Services/TripPlanner`. Any `ex.Message`-only or interpolated log that WS5/WS6 introduced is converted to `Log<SameLevel>(ex, template, args)`, adding `{StatusCode}` for `TripPlannerException`. |
| `SlackStatusApp` `LogInformation($"SlackApp: {e.ToString()}")` (not in the CR list, found by grep) | Present; WS5 Task 4 rewrites the file without it | Convert to `LogInformation("SlackApp: {SlackEvent}", e)` if it is still present |

### CR-44 (TripSummary)

- **D13:** `ToString` uses `{(int)TravelTime.TotalMinutes} mins`. TDD with a 75-minute trip.
- **D14 `TripSummary.Factory(time, place)` ignores `place`:**
  - The WS6 spec §2 explicitly leaves this to WS8, so it is absorbed.
  - Fix: `Origin = TimePlace.Factory(time, place)`.
  - `Destination` stays a default `TimePlace`. The intended destination is unspecified, and the only production caller (`TripTimerController.TestTimingConfig`) passes a time only and reads `Origin`, so nothing visible changes.
  - Tests: un-skip `Factory_SetsOriginPlace_FromSuppliedPlaceArgument` and replace `Factory_CurrentBehaviour_...` with a test pinning the default destination.

## 5. Deferred minors from earlier reviews

| Source ledger | Item | Decision |
|---|---|---|
| WS3 (M3, re-deferred by WS4) | 10 s per-app dispose timeout not injectable; timeout path untested | **Absorbed (D15).** Keep the constant as `internal static readonly TimeSpan DefaultAppDisposeTimeout` (10 s). Add `internal TimeSpan AppDisposeTimeout { get; set; }` on the instance, defaulting to it. A property rather than a constructor parameter, so WS7's constructor change does not conflict. A test covers the path: a hung app with a 50 ms timeout, and StopAsync still disposes the others. |
| WS3 | Unknown-app-type warning lists `ButtonApp` as a valid type (it is auto-created per MQTT device, not configurable) | **Absorbed (D16).** Add `AppNames.Configurable` (All minus ButtonApp) and use it in the warning. `AppNames.All` and its test stay as they are. |
| WS3 | Failed init adds up to 10 s to startup; double-click test uses the real clock | Left. The first is by design; the second is WS5 (CR-32). |
| WS2 | MqttConnector instances undisposed in 2 test files | **Absorbed (D17).** The real-client instances are in `test/Test/Services/FakePublishers.cs` (`FakeMqttPublisher`) and `test/Test/Apps/SlackStatus/SlackStatusAppTests.cs`. WS5 rewrites the second onto mocks. `FakeMqttPublisher` now builds its connector over a `FakeMqttClient`, so no MQTTnet client is created. If `SlackStatusAppTests` still builds a real connector, apply the same change there. |
| WS2 | Boot test starts a real 1 s reconnect delay | **Absorbed (D18).** `MqttConnectorTests.CreateConnector` injects a delay that waits only for cancellation, the same pattern `MqttConnectorReconnectTests` uses. No test in that file awaits `ReconnectTask`. |
| WS2 | ReconnectTask overwrite race; real MQTTnet event order untested; async handler-throw isolation; Subscribe without cancellation | Left for the owner (theoretical or needs a real broker) |
| WS1 | `AwtrixService` throws `NullReferenceException` synchronously on a null address or message | **Absorbed (D19).** Guard each public method: a null address, message, settings or rtttl logs a Warning (a new log line, not a level change) and returns `false`. `AppClear`/`Dismiss` deliberately publish a null message internally and are unaffected. TDD. |
| WS1 | Timer start/stop edge cases, extra minute tick at startup, no StopAsync-promptness test | Left for the owner |
| Overnight ledger | Skipped known-bug test "TripSummary.Factory ignores place" | **Absorbed (D14)**; WS6 excludes it |

## 6. Acceptance criteria

1. `Dockerfile`:
   - has no `8081` and no `dotnet build`;
   - copies both csproj files before `dotnet restore`;
   - runs exactly one `dotnet publish ... --no-restore ... /p:GIT_COMMIT=$GIT_COMMIT /p:GIT_COMMIT_SHORT=$GIT_COMMIT_SHORT`;
   - still has `EXPOSE 8080`, `WORKDIR /app-api` and `ENTRYPOINT ["dotnet", "awtrix-api.dll"]`.

   The two-layer publish simulation produces `awtrix-api.dll`. When Docker is available, `docker image inspect` shows ExposedPorts `{"8080/tcp":{}}`, Entrypoint `["dotnet","awtrix-api.dll"]` and WorkingDir `/app-api`.
2. `grep -rn "UseHttpsRedirection\|SlackUserHarvester\|UserDnChanged\|SlackDndChangedEventArgs\|_slackApiClient\|Newtonsoft" src --include=*.cs` finds nothing.
3. `SlackConnector` has no static fields. Stopping one instance never disconnects another's client (test).
4. `AwtrixAppMessage.ToString()` starts with `text=` when text is set (test).
5. `TripSummary` with a 75-minute trip ends with `(75 mins)`. `Factory(time, "Central").Origin.Place == "Central"`. The known-bug skip is removed.
6. `AwtrixService` returns `false` without throwing or publishing for null arguments (test).
7. Conductor gives up on a hung app after `AppDisposeTimeout` (test). The unknown-type warning lists `AppNames.Configurable` (no ButtonApp).
8. No test constructs `MqttConnector` without a `FakeMqttClient`. `MqttConnectorTests` uses no real backoff delay.
9. `grep -rnE 'Log(Trace|Debug|Information|Warning|Error|Critical)\(\$"' src --include=*.cs` finds nothing.
10. `dotnet build` shows no new warnings in touched files. `dotnet test` reports 0 failed, and the skipped count is one lower than Task 0's baseline.

## 7. Risks

- **Drift from WS5-WS7:**
  - Mitigation: Task 0 re-greps everything and each step says what to do when a match is missing.
  - Specific hotspots:
    - WS7 rewrites `Program.cs` `Main` (the `UseHttpsRedirection` location).
    - WS7 changes the `SlackConnector` constructor and token read (field edits stay local).
    - WS6 rewrites `TripPlannerService`/`TripTimerApp` (the using may vanish; refresh logging may add new `ex.Message` lines).
- **`--no-restore` publish** could fail if the publish graph differs from the restore graph (for example a RuntimeIdentifier). None is set today, and the D2 simulation proves it locally.
- **GitHub Actions layer cache:** the first CI build after the change misses the cache once. That's expected.
- **Deleting `SlackDndChangedEventArgs`** is a public type removal inside the web app assembly. Nothing outside the repo references the assembly.
- **Removing `SlackStatusAppConfig.SlackUserId`** could break an external binder that set it. Nothing in the repo does; `As<T>` copies only named members.
