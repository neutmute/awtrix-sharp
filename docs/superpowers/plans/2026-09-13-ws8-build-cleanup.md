# WS8 Build, CI and Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the Docker build layers, drop the dead HTTPS/8081 surface and Slack debug tooling, and remove the remaining dead code, small defects and test-hygiene gaps from code-review.md (CR-41 to CR-45) and the WS1-WS3 review ledgers.

**Architecture:** This is the last workstream, so every task is a small file-local edit located by grep, not line number. Task 0 re-greps every target against the tree WS5-WS7 left and drops items already fixed. Behaviour changes get TDD: SlackConnector instance state, message ordering, TripSummary, null guards, and the Conductor timeout and warning. Pure cleanup is verified with build, grep and the full test suite.

**Tech Stack:** .NET 10, ASP.NET Core, xUnit 2.9, Moq 4.20, MQTTnet 5, SlackNet 0.17, Docker multi-stage build, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-13-ws8-build-cleanup-design.md`

## Global Constraints

- CR-17 (CI test job) is done (`2a681ef`). Do not edit `.github/workflows/docker-publish.yml`.
- `appsettings.json` and env-var configuration stay compatible. No config key is added, renamed or removed.
- Docker image contract is unchanged:
  - `ENTRYPOINT ["dotnet", "awtrix-api.dll"]`
  - `WORKDIR /app-api`
  - `EXPOSE 8080`
  - `ARG GIT_COMMIT` / `ARG GIT_COMMIT_SHORT` (top level and redeclared in the publish stage)
  - `/p:GIT_COMMIT=$GIT_COMMIT /p:GIT_COMMIT_SHORT=$GIT_COMMIT_SHORT` on publish

  Only `EXPOSE 8081` is removed.
- Structured-logging conversions must keep the original log level.
- **Never** `dotnet run` the app, connect to a broker or clock, or `docker run` a container.
- Commits: explicit `git add <paths>` (or `git rm <path>`), never `git add -A` / `git add .`. Other agents commit in this tree. Every commit message ends with:

  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt
  ```
- Commands below are for Git Bash, run from the repo root `C:\CodeMine\awtrix-sharp`.
- If a grep in a step finds nothing because WS5-WS7 already fixed the item, skip that edit and note it in your report. Do not reintroduce anything.

## File map

| File | Change | Task |
|---|---|---|
| `Dockerfile` | csproj-first restore, single `publish --no-restore`, drop `EXPOSE 8081` | 1 |
| `src/api/Program.cs` | delete `app.UseHttpsRedirection();` | 1 |
| `src/api/Properties/launchSettings.json` | Container profile: drop HTTPS port 8081, `useSSL: false` | 1 |
| `src/api/Debug/SlackUserHarvester.cs` | delete | 1 |
| `src/api/HostedServices/SlackConnector.cs` | instance fields, dead members, usings, doc comment | 2 |
| `src/api/HostedServices/Slack/SlackDndChangedEventArgs.cs` | delete | 2 |
| `test/Test/HostedServices/SlackConnectorTests.cs` | new test, instance-only reflection | 2 |
| `src/api/Domain/AwtrixAppMessage.cs` | `ToString` text-first key | 3 |
| `src/api/Services/AwtrixService.cs` | null guards; unreachable Quantize branch | 3 |
| `src/api/Services/TripPlanner/TripSummary.cs` | duration format; `Factory` uses `place` | 3 |
| `test/Test/Domain/AwtrixAppMessageBuilderTests.cs`, `test/Test/Services/AwtrixServicePublishTests.cs`, `test/Test/Services/AwtrixServiceTests.cs`, `test/Test/TripPlanner/TripSummaryBehaviorTests.cs` | tests | 3 |
| `src/api/HostedServices/Conductor.cs` | injectable `AppDisposeTimeout`; `AppNames.Configurable` | 4 |
| `test/Test/HostedServices/ConductorShutdownTests.cs`, `test/Test/HostedServices/ConductorTests.cs` | tests | 4 |
| `src/api/Apps/MqttRender/MqttRenderApp.cs`, `src/api/Apps/AwtrixApp.cs`, `src/api/Apps/SlackStatus/SlackStatusAppConfig.cs`, `src/api/Apps/TripTimer/TripTimerApp.cs`, `src/api/Services/TripPlanner/TripPlannerService.cs`, `src/api/Controllers/TripPlannerController.cs` (+ any file with a residual interpolated log) | dead code, comments, usings, structured logs | 5 |
| `test/Test/Services/FakePublishers.cs`, `test/Test/HostedServices/MqttConnectorTests.cs` (+ `test/Test/Apps/SlackStatus/SlackStatusAppTests.cs` only if it still builds a real connector) | test hygiene | 6 |

---

### Task 0: Baseline and re-grep targets against the current tree

**Files:** none modified. Record the results in your report.

- [ ] **Step 1: Confirm the predecessors and the baseline**

Run:
```bash
git log --oneline | head -40
dotnet build awtrix-sharp.sln -c Release 2>&1 | tail -3
dotnet test awtrix-sharp.sln -c Release 2>&1 | grep -E "Passed!|Failed!|error"
```
Expected:
- The build succeeds.
- Both test projects report `Failed: 0`.
- Record the Passed/Skipped counts for each project (e.g. `Test: Passed N, Skipped S`); Task 6 compares against them.
- Note which WS5/WS6/WS7 commits are present. If some are missing, continue anyway: every task below is grep-driven.

- [ ] **Step 2: Re-grep every target**

Run each command and write down the matches. The "Expected at 9bdf019" column shows what the tree looked like when this plan was written. WS5-WS7 may have removed some.

| # | Command | Expected at 9bdf019 | If no match |
|---|---|---|---|
| G1 | `grep -n "EXPOSE 8081\|dotnet build\|COPY src/api/awtrix-api.csproj" Dockerfile` | 3 matches | Task 1 Step 1 still replaces the whole file |
| G2 | `grep -rn "UseHttpsRedirection" src --include=*.cs` | `src/api/Program.cs` (after WS7: inside `ConfigureHttpPipeline`) | skip Task 1 Step 2 |
| G3 | `grep -rn "SlackUserHarvester" src test --include=*.cs` | only `src/api/Debug/SlackUserHarvester.cs:12` | skip Task 1 Step 4 |
| G4 | `grep -n "static ISlack\|_slackApiClient\|UserDnChanged\|using Newtonsoft\|using System.Net.Http\|using System.Text" src/api/HostedServices/SlackConnector.cs` | static `_slackApiClient`, static `_slackSocketClient`, `UserDnChanged`, Newtonsoft, `System.Net.Http`, `System.Net.Http.Headers`, `System.Text`, `System.Text.Json` | drop the matching sub-edit in Task 2 |
| G5 | `grep -rn "SlackDndChangedEventArgs" src test --include=*.cs` | the class file and `SlackConnector.cs` only | if referenced elsewhere, keep the class file (Task 2 Step 4) |
| G6 | `grep -n "kvp.Key == \"Text\"\|const string TextKey" src/api/Domain/AwtrixAppMessage.cs` | the `"Text"` comparison (plus `TextKey` after WS5) | skip Task 3 Steps 1-3 |
| G7 | `grep -n "else if (p < 4)" src/api/Services/AwtrixService.cs` | 1 match | skip Task 3 Step 8 |
| G8 | `grep -n "public Task<bool>" src/api/Services/AwtrixService.cs` | `Set`, `PlayRtttl`, `AppUpdate`, `AppClear`, `Notify`, `Dismiss` | adapt guards to the signatures present |
| G9 | `grep -n "TravelTime:mm\|Origin = TimePlace.Factory(time)" src/api/Services/TripPlanner/TripSummary.cs` | 2 matches | skip the matching Task 3 sub-step |
| G10 | `grep -n "AppDisposeTimeout\|AppNames.All)" src/api/HostedServices/Conductor.cs` | `internal static readonly TimeSpan AppDisposeTimeout`, its two uses, and `string.Join(", ", AppNames.All)` | skip the matching Task 4 sub-step |
| G11 | `grep -n "CreateLogger<" src/api/HostedServices/Conductor.cs` | the ButtonApp branch uses `CreateLogger<ButtonApp>()` (CR-42 already fixed; nothing to do) | none |
| G12 | `grep -n "message.Text == null" src/api/Apps/MqttRender/MqttRenderApp.cs` | 1 match | skip Task 5 Step 1 |
| G13 | `grep -rn "SetText(null\|Remove(\"text\"\|\[\"text\"\] = null" src/api --include=*.cs` | no matches | if any match, **keep** the MqttRenderApp check (skip Task 5 Step 1) and report why |
| G14 | `grep -n "Diurnal sending empty custom payload" src/api/Apps/AwtrixApp.cs` | 1 match | skip Task 5 Step 2 |
| G15 | `grep -rn "SlackUserId {" src/api --include=*.cs` and `grep -rn "\.SlackUserId\b" src test --include=*.cs` | `SlackStatusAppConfig.cs:7`; second grep empty | if the second grep has matches, keep the property and report |
| G16 | `grep -rn "Is an odd second" src/api --include=*.cs` | `TripTimerApp.cs` | skip Task 5 Step 4 |
| G17 | `grep -rn "Newtonsoft" src/api --include=*.cs` | `SlackConnector.cs`, `TripPlannerService.cs` | skip Task 5 Step 5 |
| G18 | `grep -rn "D:\\\\\\\\downloads" src/api --include=*.cs` | `TripPlannerController.cs` | skip Task 5 Step 6 |
| G19 | `grep -rnE 'Log(Trace|Debug|Information|Warning|Error|Critical)\(\$"' src --include=*.cs` | `Apps/SlackStatus/SlackStatusApp.cs` (WS5 removes it) | skip Task 5 Step 7 |
| G20 | `grep -rn "ex\.Message" src/api/Apps src/api/Services/TripPlanner src/api/Controllers --include=*.cs` | `Apps/Configs/ValueMap.cs` only (WS5 rewrites it) | see Task 5 Step 8 |
| G21 | `grep -rn "new MqttConnector(" test/Test --include=*.cs` | `MqttConnectorTests.cs`, `MqttConnectorReconnectTests.cs`, `MqttPublisherConnectorTests.cs` (all with `client`), `Services/FakePublishers.cs` and `Apps/SlackStatus/SlackStatusAppTests.cs` (both without a client; WS5 rewrites the latter onto mocks) | Task 6 fixes the ones without a client |
| G22 | `grep -n "Skip = \"Known bug: TripSummary.Factory" test/Test/TripPlanner/TripSummaryBehaviorTests.cs` | 1 match | skip Task 3 Step 6's un-skip |

No commit for Task 0.

---

### Task 1: Container build and HTTP surface (CR-45)

**Files:**
- Modify: `Dockerfile` (whole file)
- Modify: `src/api/Program.cs` (one line, found by grep)
- Modify: `src/api/Properties/launchSettings.json` ("Container (Dockerfile)" profile)
- Delete: `src/api/Debug/SlackUserHarvester.cs`

**Interfaces:**
- Consumes: none
- Produces: nothing code-level. Image contract unchanged (see Global Constraints).

- [ ] **Step 1: Replace the Dockerfile**

Write `Dockerfile` with exactly:

```dockerfile
# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.
ARG GIT_COMMIT
ARG GIT_COMMIT_SHORT

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app-api
EXPOSE 8080

# This stage restores (cached until a project file changes) and holds the sources
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY src/api/awtrix-api.csproj api/
COPY src/transportOpenData/TransportOpenData.csproj transportOpenData/
RUN dotnet restore api/awtrix-api.csproj
COPY src/ .

# This stage publishes the service project (one compile) to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
# Redeclare these ARGs to make them available in this stage
ARG GIT_COMMIT
ARG GIT_COMMIT_SHORT
WORKDIR /src/api
RUN dotnet publish awtrix-api.csproj -c $BUILD_CONFIGURATION --no-restore -o /app-api/publish /p:UseAppHost=false /p:GIT_COMMIT=$GIT_COMMIT /p:GIT_COMMIT_SHORT=$GIT_COMMIT_SHORT

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app-api
COPY --from=publish /app-api/publish .
ENTRYPOINT ["dotnet", "awtrix-api.dll"]
```

If WS6/WS7 added a new `ProjectReference` from `awtrix-api.csproj`, check with `grep -n "ProjectReference" src/api/awtrix-api.csproj`. Expected: only `..\transportOpenData\TransportOpenData.csproj`. Add a matching `COPY <csproj> <folder>/` line before `RUN dotnet restore` for any other project.

- [ ] **Step 2: Remove HTTPS redirection**

Find it with `grep -rn "UseHttpsRedirection" src --include=*.cs`, then delete the line `app.UseHttpsRedirection();` and the blank line directly after it. Leave the surrounding pipeline calls unchanged. If a nearby comment mentions HTTPS redirection (for example WS7's `(unchanged; WS8 removes it)`), delete that comment too.

- [ ] **Step 3: Launch profile no longer maps 8081**

In `src/api/Properties/launchSettings.json`, inside the `"Container (Dockerfile)"` profile:
- delete the line `"ASPNETCORE_HTTPS_PORTS": "8081",`
- change `"useSSL": true` to `"useSSL": false`

The profile must read:

```json
    "Container (Dockerfile)": {
      "commandName": "Docker",
      "launchUrl": "{Scheme}://{ServiceHost}:{ServicePort}",
      "environmentVariables": {
        "ASPNETCORE_HTTP_PORTS": "8080"
      },
      "publishAllPorts": true,
      "useSSL": false
    }
```

- [ ] **Step 4: Delete the Slack harvester**

```bash
git rm src/api/Debug/SlackUserHarvester.cs
```
Expected: `rm 'src/api/Debug/SlackUserHarvester.cs'`.

- [ ] **Step 5: Static checks**

```bash
grep -c "8081" Dockerfile src/api/Properties/launchSettings.json
grep -c "dotnet build" Dockerfile
grep -n 'ENTRYPOINT \["dotnet", "awtrix-api.dll"\]\|WORKDIR /app-api\|EXPOSE 8080\|^ARG GIT_COMMIT\|COPY src/transportOpenData/TransportOpenData.csproj\|--no-restore.*GIT_COMMIT=\$GIT_COMMIT /p:GIT_COMMIT_SHORT=\$GIT_COMMIT_SHORT' Dockerfile
grep -n "GIT_COMMIT=\|GIT_COMMIT_SHORT=" .github/workflows/docker-publish.yml
grep -rn "UseHttpsRedirection\|SlackUserHarvester" src --include=*.cs
```
Expected:
- The first command prints `Dockerfile:0` and `src/api/Properties/launchSettings.json:0`.
- The second prints `0`.
- The third prints these lines:
  - `ENTRYPOINT`
  - `WORKDIR /app-api` twice
  - `EXPOSE 8080`
  - `ARG GIT_COMMIT` four times (two at top level, two in `publish`)
  - the transportOpenData `COPY`
  - the publish line
- The workflow still passes `GIT_COMMIT=` and `GIT_COMMIT_SHORT=`.
- The last grep prints nothing.

- [ ] **Step 6: Simulate the two Docker layers locally (no Docker needed)**

```bash
work="$(mktemp -d)"
mkdir -p "$work/src/api" "$work/src/transportOpenData"
cp src/api/awtrix-api.csproj "$work/src/api/"
cp src/transportOpenData/TransportOpenData.csproj "$work/src/transportOpenData/"
(cd "$work/src" && dotnet restore api/awtrix-api.csproj)
tar -C src --exclude=bin --exclude=obj -cf - . | tar -C "$work/src" -xf -
(cd "$work/src/api" && dotnet publish awtrix-api.csproj -c Release --no-restore -o "$work/publish" /p:UseAppHost=false /p:GIT_COMMIT=0000000000000000000000000000000000000000 /p:GIT_COMMIT_SHORT=0000000)
ls "$work/publish/awtrix-api.dll" "$work/publish/TransportOpenData.dll"
rm -rf "$work"
```
Expected:
- The restore succeeds with only the two project files present (no `NU1105`/project-not-found error).
- The publish succeeds without restoring (no `NETSDK1004 assets file not found`).
- Both dlls are listed.

- [ ] **Step 7: Docker build (run if Docker is available)**

```bash
if docker info >/dev/null 2>&1; then
  docker build --build-arg GIT_COMMIT="$(git rev-parse HEAD)" --build-arg GIT_COMMIT_SHORT="$(git rev-parse --short HEAD)" -t awtrix-sharp:ws8-check . &&
  docker image inspect awtrix-sharp:ws8-check --format '{{json .Config.ExposedPorts}} {{json .Config.Entrypoint}} {{.Config.WorkingDir}}' &&
  docker image rm awtrix-sharp:ws8-check
else
  echo "docker unavailable: rely on Steps 5-6"
fi
```
Expected when Docker is available: `{"8080/tcp":{}} ["dotnet","awtrix-api.dll"] /app-api`. **Do not `docker run` the image.**

- [ ] **Step 8: Build and full test**

```bash
dotnet build awtrix-sharp.sln -c Release 2>&1 | tail -3
dotnet test awtrix-sharp.sln -c Release 2>&1 | grep -E "Passed!|Failed!|error"
```
Expected: the build succeeds, `Failed: 0` in both projects, and the counts match the Task 0 baseline.

- [ ] **Step 9: Commit**

```bash
git add Dockerfile src/api/Program.cs src/api/Properties/launchSettings.json
git commit -m "$(cat <<'EOF'
build(docker): csproj-first restore, single publish; drop 8081, HTTPS redirection and Slack harvester (CR-45)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt
EOF
)"
```
(The harvester deletion was already staged by `git rm` in Step 4.)

---

### Task 2: SlackConnector instance state and dead members (CR-41)

**Files:**
- Modify: `src/api/HostedServices/SlackConnector.cs`
- Delete: `src/api/HostedServices/Slack/SlackDndChangedEventArgs.cs`
- Test: `test/Test/HostedServices/SlackConnectorTests.cs`

**Interfaces:**
- Consumes: `SlackNet.SocketMode.ISlackSocketModeClient` (`void Disconnect()`), the existing `SlackConnector(ILogger<SlackConnector> logger[, IOptions<SlackSettings>? slackSettings = null])` constructor. The second parameter exists only after WS7.
- Produces: `private ISlackSocketModeClient? _slackSocketClient` is an **instance** field (tests reach it by reflection by that name). `UserDnChanged` and `SlackDndChangedEventArgs` no longer exist.

- [ ] **Step 1: Write the failing test**

In `test/Test/HostedServices/SlackConnectorTests.cs`:
1. Add `using Moq;` and `using SlackNet.SocketMode;` at the top.
2. Change the `AnyField` constant to instance-only.
3. Add a `SetField` helper and the new test.

The class body becomes:

```csharp
    public class SlackConnectorTests
    {
        private const BindingFlags InstanceField = BindingFlags.NonPublic | BindingFlags.Instance;

        private static void SetField(SlackConnector connector, string name, object? value)
        {
            var field = typeof(SlackConnector).GetField(name, InstanceField);
            Assert.NotNull(field); // CR-41: connector state is per instance, never static
            field!.SetValue(connector, value);
        }

        [Fact]
        public async Task StopAsync_WhenSlackWasNeverConnected_DoesNotThrow()
        {
            var connector = new SlackConnector(NullLogger<SlackConnector>.Instance);
            // State after StartAsync ran without an app token: the executing task finished early and
            // no socket client was created. Set directly so the test ignores the developer's environment.
            SetField(connector, "_executingTask", Task.CompletedTask);
            SetField(connector, "_slackSocketClient", null);

            var exception = await Record.ExceptionAsync(() => connector.StopAsync(CancellationToken.None));

            Assert.Null(exception);
        }

        [Fact]
        public async Task StopAsync_BeforeStart_DoesNotThrow()
        {
            var connector = new SlackConnector(NullLogger<SlackConnector>.Instance);

            var exception = await Record.ExceptionAsync(() => connector.StopAsync(CancellationToken.None));

            Assert.Null(exception);
        }

        [Fact]
        public async Task StopAsync_DoesNotDisconnectAnotherInstancesSocketClient()
        {
            var socket = new Mock<ISlackSocketModeClient>();
            var first = new SlackConnector(NullLogger<SlackConnector>.Instance);
            SetField(first, "_slackSocketClient", socket.Object);
            var second = new SlackConnector(NullLogger<SlackConnector>.Instance);
            SetField(second, "_executingTask", Task.CompletedTask);

            await second.StopAsync(CancellationToken.None);

            socket.Verify(s => s.Disconnect(), Times.Never);
        }

        [Fact]
        public void DeadMembers_AreRemoved()
        {
            Assert.Null(typeof(SlackConnector).GetField("_slackApiClient", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static));
            Assert.Null(typeof(SlackConnector).GetEvent("UserDnChanged"));
        }
    }
```

If WS7 changed how the constructor is called in this file, keep WS7's call form. The single-argument form stays valid, because WS7's parameter is optional.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~SlackConnectorTests"`
Expected: FAIL.
- `SetField`'s `Assert.NotNull(field)` fails for `_slackSocketClient`, because the field is static and the lookup is instance-only.
- `DeadMembers_AreRemoved` fails.

- [ ] **Step 3: Make the state per instance and remove the dead members**

In `src/api/HostedServices/SlackConnector.cs`:

1. Delete these using lines if present (grep G4). Keep every other using, including any WS7 added:
   ```csharp
   using Newtonsoft.Json.Linq;
   using System.Net.Http;
   using System.Net.Http.Headers;
   using System.Text;
   using System.Text.Json;
   ```
2. Replace
   ```csharp
           private static ISlackApiClient _slackApiClient;
           private static ISlackSocketModeClient _slackSocketClient;
   ```
   with
   ```csharp
           private ISlackSocketModeClient? _slackSocketClient;
   ```
3. Delete
   ```csharp
           public event EventHandler<SlackDndChangedEventArgs>? UserDnChanged;

   ```
4. Replace the socket-client creation, the commented-out API client and the connect call. Find them with `grep -n "_slackSocketClient = new SlackServiceBuilder\|//_slackApiClient\|await _slackSocketClient.Connect" src/api/HostedServices/SlackConnector.cs`.
   - Replace
     ```csharp
                     _slackSocketClient = new SlackServiceBuilder()
                                         .UseAppLevelToken(appToken)
                                         .RegisterEventHandler(this)
                                         .GetSocketModeClient();

                     //_slackApiClient = new SlackServiceBuilder()
                     //    .UseApiToken(appToken) // xoxp for user scopes, or xoxb with proper scopes
                     //    .GetApiClient();

     ```
     with
     ```csharp
                     var socketClient = new SlackServiceBuilder()
                                         .UseAppLevelToken(appToken)
                                         .RegisterEventHandler(this)
                                         .GetSocketModeClient();
                     _slackSocketClient = socketClient;

     ```
   - Replace `await _slackSocketClient.Connect(socketModeOptions, stoppingToken);` with `await socketClient.Connect(socketModeOptions, stoppingToken);`
5. Directly above `public class SlackConnector`, add:
   ```csharp
       /// <summary>
       /// Slack Socket Mode listener that raises <see cref="UserStatusChanged"/> for user_change events.
       /// Do-not-disturb (DND) changes are not supported: no DND event is subscribed to or raised.
       /// After the first successful connect, reconnection is left to SlackNet's socket-mode client.
       /// </summary>
   ```
6. Remove the empty line directly after `namespace AwtrixSharpWeb.HostedServices` and `{`, if one is there.

- [ ] **Step 4: Delete the unused DND event args**

Only if grep G5 showed no other references:
```bash
git rm src/api/HostedServices/Slack/SlackDndChangedEventArgs.cs
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet build awtrix-sharp.sln -c Release 2>&1 | grep -E "SlackConnector.cs.*(warning|error)|Build succeeded|error"
dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~SlackConnectorTests"
```
Expected: the build succeeds with no warnings or errors from `SlackConnector.cs`, and all 4 SlackConnectorTests pass. If a removed using was actually needed (a compile error names the type), restore just that using.

- [ ] **Step 6: Full test and grep**

```bash
dotnet test awtrix-sharp.sln -c Release 2>&1 | grep -E "Passed!|Failed!|error"
grep -rn "UserDnChanged\|SlackDndChangedEventArgs\|_slackApiClient\|static ISlack" src --include=*.cs
```
Expected: `Failed: 0`, and the grep prints nothing.

- [ ] **Step 7: Commit**

```bash
git add src/api/HostedServices/SlackConnector.cs test/Test/HostedServices/SlackConnectorTests.cs
git commit -m "$(cat <<'EOF'
refactor(slack): per-instance socket client; remove dead DND event, API client and usings (CR-41)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt
EOF
)"
```
(The `SlackDndChangedEventArgs.cs` deletion was staged by `git rm` in Step 4.)

---

### Task 3: Message ordering, publish null guards, Quantize, TripSummary (CR-42, CR-44, WS1 deferred)

**Files:**
- Modify: `src/api/Domain/AwtrixAppMessage.cs` (`ToString`)
- Modify: `src/api/Services/AwtrixService.cs` (public publish methods, `Quantize`, new private helper)
- Modify: `src/api/Services/TripPlanner/TripSummary.cs` (`ToString`, `Factory`)
- Test: `test/Test/Domain/AwtrixAppMessageBuilderTests.cs`, `test/Test/Services/AwtrixServicePublishTests.cs`, `test/Test/Services/AwtrixServiceTests.cs`, `test/Test/TripPlanner/TripSummaryBehaviorTests.cs`

**Interfaces:**
- Consumes:
  - `AwtrixAppMessage.SetText/SetIcon/SetColor` (fluent)
  - `IAwtrixService` methods `Set(AwtrixAddress, AwtrixSettings)`, `PlayRtttl(AwtrixAddress, string)`, `AppUpdate(AwtrixAddress, string, AwtrixAppMessage)`, `AppClear(AwtrixAddress, string)`, `Notify(AwtrixAddress, AwtrixAppMessage)`, `Dismiss(AwtrixAddress)`, all returning `Task<bool>`
  - test fakes `FakeHttpPublisher` / `FakeMqttPublisher` with `PublishCallCount`
  - `TimePlace.Factory(DateTimeOffset, string)`
- Produces: the same public signatures; null arguments now give `false` instead of throwing. `TripSummary.Factory(time, place)` sets `Origin.Place`.

- [ ] **Step 1: Write the failing ordering test**

In `test/Test/Domain/AwtrixAppMessageBuilderTests.cs`, add directly after `ToString_OmitsNothingAndJoinsWithSemicolons`:

```csharp
        [Fact]
        public void ToString_ListsTextFirst()
        {
            var message = new AwtrixAppMessage()
                .SetIcon("1")
                .SetColor("#FF0000")
                .SetText("Hello");

            Assert.StartsWith("text=Hello; ", message.ToString());
        }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~AwtrixAppMessageBuilderTests.ToString_ListsTextFirst"`
Expected: FAIL. The actual string starts with `color=#FF0000; `.

- [ ] **Step 3: Fix the key comparison**

In `src/api/Domain/AwtrixAppMessage.cs`, replace
```csharp
                this.OrderBy(kvp => kvp.Key == "Text" ? "" : kvp.Key)       // always name first
```
with (if `private const string TextKey = "text";` exists, from WS5)
```csharp
                this.OrderBy(kvp => kvp.Key == TextKey ? "" : kvp.Key, StringComparer.Ordinal)       // always text first
```
or, if there is no `TextKey` constant,
```csharp
                this.OrderBy(kvp => kvp.Key == "text" ? "" : kvp.Key, StringComparer.Ordinal)       // always text first
```

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~AwtrixAppMessage"`
Expected: PASS (all AwtrixAppMessage tests).

- [ ] **Step 4: Write the failing null-guard tests**

In `test/Test/Services/AwtrixServicePublishTests.cs`, add at the end of the class:

```csharp
        [Fact]
        public async Task NullAddress_ReturnsFalse_WithoutThrowingOrPublishing()
        {
            var (service, http, mqtt) = CreateService();
            var message = new AwtrixAppMessage().SetText("Hello");

            Assert.False(await service.Notify(null!, message));
            Assert.False(await service.Dismiss(null!));
            Assert.False(await service.AppUpdate(null!, "MyApp", message));
            Assert.False(await service.AppClear(null!, "MyApp"));
            Assert.False(await service.Set(null!, new AwtrixSettings()));
            Assert.False(await service.PlayRtttl(null!, "tune:d=4,o=5,b=100:c"));

            Assert.Equal(0, mqtt.PublishCallCount);
            Assert.Equal(0, http.PublishCallCount);
        }

        [Fact]
        public async Task NullPayloadArgument_ReturnsFalse_WithoutThrowingOrPublishing()
        {
            var (service, http, mqtt) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "awtrix/clock1" };

            Assert.False(await service.Notify(address, null!));
            Assert.False(await service.AppUpdate(address, "MyApp", null!));
            Assert.False(await service.Set(address, null!));
            Assert.False(await service.PlayRtttl(address, null!));

            Assert.Equal(0, mqtt.PublishCallCount);
            Assert.Equal(0, http.PublishCallCount);
        }
```

If `new AwtrixSettings()` does not compile, build the settings the same way `Set_PublishesSettingsJsonToSettingsTopic` in this file does.

- [ ] **Step 5: Run them to verify they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~AwtrixServicePublishTests.Null"`
Expected: FAIL.
- `NullAddress...` fails with `NullReferenceException`.
- `NullPayloadArgument...` fails with `NullReferenceException` from `Notify`. After that is guarded, `AppUpdate` would return `true` with a publish.

- [ ] **Step 6: Add the guards**

In `src/api/Services/AwtrixService.cs`, add this private helper directly above `SafePublish`:

```csharp
        /// <summary>
        /// WS1 deferred: a null argument is a caller bug, but publishing must never throw. Logs and reports "not delivered".
        /// </summary>
        private bool IsNull(object? argument, string argumentName, string operation)
        {
            if (argument != null)
            {
                return false;
            }

            _logger.LogWarning("{Operation} called with a null {Argument}; nothing published", operation, argumentName);
            return true;
        }
```

Make each public publish method's first statement the guard shown below. Leave the rest of each method body unchanged.

- `Set`:
  ```csharp
              if (IsNull(awtrixAddress, nameof(awtrixAddress), nameof(Set)) || IsNull(settings, nameof(settings), nameof(Set)))
              {
                  return Task.FromResult(false);
              }
  ```
- `PlayRtttl`:
  ```csharp
              if (IsNull(awtrixAddress, nameof(awtrixAddress), nameof(PlayRtttl)) || IsNull(rtttl, nameof(rtttl), nameof(PlayRtttl)))
              {
                  return Task.FromResult(false);
              }
  ```
- `AppUpdate`:
  ```csharp
              if (IsNull(awtrixAddress, nameof(awtrixAddress), nameof(AppUpdate)) || IsNull(message, nameof(message), nameof(AppUpdate)))
              {
                  return Task.FromResult(false);
              }
  ```
- `AppClear`:
  ```csharp
              if (IsNull(awtrixAddress, nameof(awtrixAddress), nameof(AppClear)))
              {
                  return Task.FromResult(false);
              }
  ```
- `Notify` (before the `String.IsNullOrWhiteSpace(message.Text)` check):
  ```csharp
              if (IsNull(awtrixAddress, nameof(awtrixAddress), nameof(Notify)) || IsNull(message, nameof(message), nameof(Notify)))
              {
                  return Task.FromResult(false);
              }
  ```
- `Dismiss`:
  ```csharp
              if (IsNull(awtrixAddress, nameof(awtrixAddress), nameof(Dismiss)))
              {
                  return Task.FromResult(false);
              }
  ```

If a method is `async` in the then-current tree (check grep G8), use `return false;` instead of `return Task.FromResult(false);`.

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~AwtrixService"`
Expected: PASS.

- [ ] **Step 7: Write the failing TripSummary tests**

In `test/Test/TripPlanner/TripSummaryBehaviorTests.cs`:

1. Replace
   ```csharp
           [Fact(Skip = "Known bug: TripSummary.Factory(time, place) ignores the 'place' parameter entirely - Origin.Place is always empty and Destination is always a default TimePlace rather than being derived from the supplied time/place.")]
           public void Factory_SetsOriginPlace_FromSuppliedPlaceArgument()
   ```
   with
   ```csharp
           [Fact]
           public void Factory_SetsOriginPlace_FromSuppliedPlaceArgument()
   ```
   and in that test change the comment `// Assert (intended behaviour - currently Origin.Place is always string.Empty)` to `// Assert`.
2. Replace the whole `Factory_CurrentBehaviour_SetsOriginTimeOnly_DestinationIsDefault` test (attribute, method and body) with:
   ```csharp
           [Fact]
           public void Factory_LeavesDestinationAsDefaultTimePlace()
           {
               var summary = TripSummary.Factory(DateTimeOffset.Parse("2025-09-01T06:41:00+10:00"), "Central");

               Assert.Equal(new TimePlace().Time, summary.Destination.Time);
               Assert.Equal(string.Empty, summary.Destination.Place);
           }
   ```
3. Add after `ToString_FormatsOriginAndDestination`:
   ```csharp
           [Theory]
           [InlineData(37, "(37 mins)")]
           [InlineData(75, "(75 mins)")]
           [InlineData(120, "(120 mins)")]
           public void ToString_ShowsTotalMinutes_ForTripsOfAnHourOrMore(int minutes, string expectedSuffix)
           {
               var origin = DateTimeOffset.Parse("2025-08-19T06:00:00+10:00");
               var sut = new TripSummary
               {
                   Origin = TimePlace.Factory(origin, "Central"),
                   Destination = TimePlace.Factory(origin.AddMinutes(minutes), "Newcastle Interchange")
               };

               Assert.EndsWith(expectedSuffix, sut.ToString());
           }
   ```

If WS6 changed `TripSummary` to require constructor arguments or `required` members, build the objects the way WS6's `TripSummaryTests.Create` (in `test/Test/Domain/TripSummaryTests.cs`) does, and keep the assertions.

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~TripSummaryBehaviorTests"`
Expected: FAIL.
- `Factory_SetsOriginPlace_FromSuppliedPlaceArgument` gets `""`, not `"Central"`.
- `ToString_ShowsTotalMinutes...(75)` gets `(15 mins)`, and `(120)` gets `(00 mins)`.

- [ ] **Step 8: Fix TripSummary and remove the unreachable Quantize branch**

In `src/api/Services/TripPlanner/TripSummary.cs`:
- replace `return $"{Origin} -> {Destination} ({TravelTime:mm} mins)";` with `return $"{Origin} -> {Destination} ({(int)TravelTime.TotalMinutes} mins)";`
- replace `Origin = TimePlace.Factory(time),` with `Origin = TimePlace.Factory(time, place),`

In `src/api/Services/AwtrixService.cs`, delete the line (grep G7):
```csharp
            else if (p < 4) blink = 1; // one bin lower
```
It is unreachable, because `n == 0` exactly when `p < 4`, and that case is handled first.

In `test/Test/Services/AwtrixServiceTests.cs`, add two rows to the `Quantise` theory after `[InlineData(4, 4, 3)]`:
```csharp
        [InlineData(5, 5, 3)]
        [InlineData(50, 50, 47)]
```

- [ ] **Step 9: Run the tests to verify they pass**

```bash
dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~TripSummary|FullyQualifiedName~AwtrixService|FullyQualifiedName~AwtrixAppMessage|FullyQualifiedName~TripTimerController"
dotnet test awtrix-sharp.sln -c Release 2>&1 | grep -E "Passed!|Failed!|error"
```
Expected: all pass. The full run shows `Failed: 0`, and the `Test` Skipped count is one lower than the Task 0 baseline.

- [ ] **Step 10: Commit**

```bash
git add src/api/Domain/AwtrixAppMessage.cs src/api/Services/AwtrixService.cs src/api/Services/TripPlanner/TripSummary.cs test/Test/Domain/AwtrixAppMessageBuilderTests.cs test/Test/Services/AwtrixServicePublishTests.cs test/Test/Services/AwtrixServiceTests.cs test/Test/TripPlanner/TripSummaryBehaviorTests.cs
git commit -m "$(cat <<'EOF'
fix(domain): text-first message ToString, null-safe publish, total-minute trip duration, Factory keeps place (CR-42, CR-44)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt
EOF
)"
```

---

### Task 4: Conductor dispose timeout seam and unknown-type warning (WS3 deferred)

**Files:**
- Modify: `src/api/HostedServices/Conductor.cs` (`AppNames`, the timeout member, the unknown-type warning)
- Test: `test/Test/HostedServices/ConductorShutdownTests.cs`, `test/Test/HostedServices/ConductorTests.cs`

**Interfaces:**
- Consumes:
  - `ConductorTestHelper.Create(...)` and `ConductorTestHelper.MockApp(string baseTopic, string type)`
  - `Conductor.RegisterApp(IAwtrixApp)` (internal) and `Conductor.StopAsync(CancellationToken)`
- Produces:
  - `internal static readonly TimeSpan Conductor.DefaultAppDisposeTimeout` (10 s)
  - `internal TimeSpan Conductor.AppDisposeTimeout { get; set; }` (instance, defaults to `DefaultAppDisposeTimeout`)
  - `public static readonly string[] AppNames.Configurable`

- [ ] **Step 1: Write the failing tests**

In `test/Test/HostedServices/ConductorShutdownTests.cs`, replace the `AppDisposeTimeout_CoversTwoHttpPublishTimeouts` test with:

```csharp
        [Fact]
        public void AppDisposeTimeout_DefaultsToTwoHttpPublishTimeouts()
        {
            Assert.Equal(TimeSpan.FromSeconds(10), Conductor.DefaultAppDisposeTimeout);
            Assert.Equal(Conductor.DefaultAppDisposeTimeout, ConductorTestHelper.Create().AppDisposeTimeout);
        }

        [Fact]
        public async Task StopAsync_WhenAnAppDisposeHangs_GivesUpAfterTheTimeout_AndDisposesOthers()
        {
            var conductor = ConductorTestHelper.Create();
            conductor.AppDisposeTimeout = TimeSpan.FromMilliseconds(50);
            var hung = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.TripTimerApp);
            hung.Setup(a => a.DisposeAsync()).Returns(new ValueTask(new TaskCompletionSource().Task));
            var healthy = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.DiurnalApp);
            conductor.RegisterApp(hung.Object);
            conductor.RegisterApp(healthy.Object);

            var exception = await Record.ExceptionAsync(
                () => conductor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Null(exception);
            hung.Verify(a => a.DisposeAsync(), Times.Once);
            healthy.Verify(a => a.DisposeAsync(), Times.Once);
        }
```

In `test/Test/HostedServices/ConductorTests.cs`, add after `AppNamesAll_ListsEveryFactoryType`:

```csharp
        [Fact]
        public void AppNamesConfigurable_ListsConfigTypes_WithoutAutoCreatedButtonApp()
        {
            Assert.Equal(
                new[] { AppNames.DiurnalApp, AppNames.TripTimerApp, AppNames.SlackStatusApp, AppNames.MqttRenderApp, AppNames.MqttClockRenderApp },
                AppNames.Configurable);
        }
```

If WS7 made `ConductorTestHelper.Create` take new optional parameters, the calls above still compile. Keep them as written.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~ConductorShutdownTests|FullyQualifiedName~ConductorTests"`
Expected: FAIL at compile time: `'Conductor' does not contain a definition for 'DefaultAppDisposeTimeout'`, and the same for `AppNames.Configurable`.

- [ ] **Step 3: Implement**

In `src/api/HostedServices/Conductor.cs`:

1. In `internal class AppNames`, replace
   ```csharp
           /// <summary>
           /// Every Type the factory can build, listed in "unknown app type" warnings.
           /// </summary>
           public static readonly string[] All = { DiurnalApp, ButtonApp, TripTimerApp, SlackStatusApp, MqttRenderApp, MqttClockRenderApp };
   ```
   with
   ```csharp
           /// <summary>
           /// Every Type the factory can build.
           /// </summary>
           public static readonly string[] All = { DiurnalApp, ButtonApp, TripTimerApp, SlackStatusApp, MqttRenderApp, MqttClockRenderApp };

           /// <summary>
           /// Types a device's Apps list may name, listed in "unknown app type" warnings. ButtonApp is created
           /// automatically for every MQTT device, so it is not offered as a configurable type.
           /// </summary>
           public static readonly string[] Configurable = All.Where(type => type != ButtonApp).ToArray();
   ```
2. Replace
   ```csharp
           /// <summary>
           /// Per-app disposal budget: two sequential 5 s HTTP publishes (Dismiss + AppClear) to an offline device.
           /// </summary>
           internal static readonly TimeSpan AppDisposeTimeout = TimeSpan.FromSeconds(10);
   ```
   with
   ```csharp
           /// <summary>
           /// Default per-app disposal budget: two sequential 5 s HTTP publishes to an offline device.
           /// </summary>
           internal static readonly TimeSpan DefaultAppDisposeTimeout = TimeSpan.FromSeconds(10);

           /// <summary>
           /// Per-app disposal budget used by <see cref="StopAsync"/>. Internal and settable so tests can exercise the timeout path.
           /// </summary>
           internal TimeSpan AppDisposeTimeout { get; set; } = DefaultAppDisposeTimeout;
   ```
   (If WS4 changed the summary text, keep only the rename and the new property. The two existing uses in `DisposeOneAsync` now read the instance property with no further edit, because `DisposeOneAsync` is an instance method. Check with `grep -n "static async Task DisposeOneAsync" src/api/HostedServices/Conductor.cs`, which should print nothing.)
3. In the unknown-type warning, replace `string.Join(", ", AppNames.All));` with `string.Join(", ", AppNames.Configurable));`

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Conductor"
dotnet test awtrix-sharp.sln -c Release 2>&1 | grep -E "Passed!|Failed!|error"
```
Expected: all Conductor tests pass, and the new timeout test finishes in well under 1 s. The full run shows `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/api/HostedServices/Conductor.cs test/Test/HostedServices/ConductorShutdownTests.cs test/Test/HostedServices/ConductorTests.cs
git commit -m "$(cat <<'EOF'
fix(conductor): testable per-app dispose timeout; unknown-type warning omits auto-created ButtonApp

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt
EOF
)"
```

---

### Task 5: Dead code, misleading comments, unused usings and structured logs (CR-43)

Pure cleanup, with no behaviour change. Verification is build, grep and the full test suite. Skip any step whose Task 0 grep came back empty.

**Files:**
- Modify: `src/api/Apps/MqttRender/MqttRenderApp.cs`, `src/api/Apps/AwtrixApp.cs`, `src/api/Apps/SlackStatus/SlackStatusAppConfig.cs`, `src/api/Apps/TripTimer/TripTimerApp.cs`, `src/api/Services/TripPlanner/TripPlannerService.cs`, `src/api/Controllers/TripPlannerController.cs`
- Modify only if G19/G20 matched: the file each match names

**Interfaces:**
- Consumes: none
- Produces: `SlackStatusAppConfig` has no `SlackUserId` property; the `"SlackUserId"` config dictionary key is unchanged.

- [ ] **Step 1: MqttRenderApp unreachable text fallback (skip if G12 empty, or if G13 matched)**

In `src/api/Apps/MqttRender/MqttRenderApp.cs`, delete this block and the blank line before it:
```csharp
                // If no text is set in the mapping, use the original status text
                if (message.Text == null)
                {
                    message.SetText(textPayload);
                }
```
The message is created with `.SetText(textPayload)`, and a ValueMap only ever sets `text` from a config string.

- [ ] **Step 2: AwtrixApp guard comment (skip if G14 empty)**

In `src/api/Apps/AwtrixApp.cs`, replace
```csharp
                // Diurnal sending empty custom payload causes errors
```
with
```csharp
                // No Type means no custom app slot to clear (publishing would target ".../custom/")
```
Keep the `if (Config.Name == null)` guard. It is covered by `InitAsync_WithNullName_SkipsAppClear_ButStillInitializes` and `AppClear_WithNullConfigName_ReturnsFalseWithoutCallingService` (spec D11).

- [ ] **Step 3: Remove the never-populated SlackUserId property (skip if G15's second grep had matches)**

Replace the body of `src/api/Apps/SlackStatus/SlackStatusAppConfig.cs` with:
```csharp
using AwtrixSharpWeb.Apps.Configs;

namespace AwtrixSharpWeb.Apps.SlackStatus
{
    /// <summary>
    /// SlackStatusApp configuration. The tracked user id is read from the "SlackUserId" key of <see cref="AppConfig.Config"/>.
    /// </summary>
    public class SlackStatusAppConfig : AppConfig
    {
    }
}
```
If WS5 or WS7 added members to this class, keep them and delete only the `SlackUserId` property line.

- [ ] **Step 4: Correct the inverted TripTimer comment (skip if G16 empty)**

In `src/api/Apps/TripTimer/TripTimerApp.cs`, replace
```csharp
            if (clockText.Contains(":"))    // Is an odd second
```
with
```csharp
            if (clockText.Contains(":"))    // Even second: FormatClockString shows the colon
```

- [ ] **Step 5: Unused Newtonsoft using (skip if G17 empty)**

In `src/api/Services/TripPlanner/TripPlannerService.cs`, delete the line `using Newtonsoft.Json.Linq;`. If WS6 moved code into another file that also has this using (G17 lists it), delete it there too and add that path to the commit.

- [ ] **Step 6: Commented-out fixture writer (skip if G18 empty)**

In `src/api/Controllers/TripPlannerController.cs`, delete these lines and the blank line after them:
```csharp
                //For populating unit tests with real data
                //var options = new JsonSerializerOptions { WriteIndented = true };
                //string json = JsonSerializer.Serialize(result, options);
                //System.IO.File.WriteAllText("D:\\downloads\\departures.json", json);
```
Then run `grep -n "JsonSerializer\|using System.Text.Json" src/api/Controllers/TripPlannerController.cs`. If the only remaining match is `using System.Text.Json;`, delete that using as well.

- [ ] **Step 7: Interpolated log calls (skip if G19 empty)**

For each match of
`grep -rnE 'Log(Trace|Debug|Information|Warning|Error|Critical)\(\$"' src --include=*.cs`,
rewrite it as a message template with named placeholders at the **same level**. For the known one in `SlackStatusApp.cs`, replace
```csharp
                Logger.LogInformation($"SlackApp: {e.ToString()}");
```
with
```csharp
                Logger.LogInformation("SlackApp: {SlackEvent}", e);
```
General rule:
- `LogX($"text {a} more {b}")` becomes `LogX("text {A} more {B}", a, b)`.
- `LogX($"...{ex}", ex)` becomes `LogX(ex, "...")`.

- [ ] **Step 8: Exception-losing logs in app and trip code (only for G20 matches outside `ValueMap.cs`)**

`ValueMap.cs` warnings are WS5's rewrite and are not listed in CR-43; leave them. For any `ex.Message` log that WS5/WS6 introduced under `src/api/Apps`, `src/api/Services/TripPlanner` or `src/api/Controllers`, pass the exception and keep the level. For example:
```csharp
                Logger.LogWarning("Departure refresh failed: {Error}", ex.Message);
```
becomes
```csharp
                Logger.LogWarning(ex, "Departure refresh failed (status {StatusCode})", (ex as TransportOpenData.TripPlanner.TripPlannerException)?.StatusCode);
```
Use the `StatusCode` form only where the caught exception can be a `TripPlannerException`. Check with `grep -rn "class TripPlannerException" src/transportOpenData`. Otherwise use `Logger.LogX(ex, "<same text without the {Error} placeholder>", <other args>)`.

- [ ] **Step 9: Build, grep and full test**

```bash
dotnet build awtrix-sharp.sln -c Release 2>&1 | tail -3
grep -rnE 'Log(Trace|Debug|Information|Warning|Error|Critical)\(\$"' src --include=*.cs
grep -rn "Newtonsoft\|D:\\\\\\\\downloads\|Is an odd second\|Diurnal sending empty custom payload\|SlackUserId {" src --include=*.cs
dotnet test awtrix-sharp.sln -c Release 2>&1 | grep -E "Passed!|Failed!|error"
```
Expected: the build succeeds, both greps print nothing, and `Failed: 0` with counts unchanged from Task 4.

- [ ] **Step 10: Commit**

Add only the files you actually changed from this list, plus any file named by G17/G19/G20 that you edited:
```bash
git add src/api/Apps/MqttRender/MqttRenderApp.cs src/api/Apps/AwtrixApp.cs src/api/Apps/SlackStatus/SlackStatusAppConfig.cs src/api/Apps/TripTimer/TripTimerApp.cs src/api/Services/TripPlanner/TripPlannerService.cs src/api/Controllers/TripPlannerController.cs
git commit -m "$(cat <<'EOF'
refactor: remove dead code, misleading comments and unused usings; structured exception logging (CR-43)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt
EOF
)"
```
(`git add` on an unmodified path is a harmless no-op.)

---

### Task 6: MQTT test hygiene (WS2 deferred)

Test-only cleanup. Verification is the affected test classes plus the full suite.

**Files:**
- Modify: `test/Test/Services/FakePublishers.cs` (`FakeMqttPublisher` constructor)
- Modify: `test/Test/HostedServices/MqttConnectorTests.cs` (`CreateConnector`)
- Modify only if G21 still lists it: `test/Test/Apps/SlackStatus/SlackStatusAppTests.cs`

**Interfaces:**
- Consumes:
  - `internal MqttConnector(ILogger<MqttConnector>, IOptions<MqttSettings>, IMqttClient client, Func<TimeSpan, CancellationToken, Task>? delay = null)`
  - `internal sealed class Test.HostedServices.FakeMqttClient : IMqttClient`
- Produces: no test creates a real MQTTnet client, and `MqttConnectorTests` never starts a real backoff timer.

- [ ] **Step 1: FakeMqttPublisher over a fake client**

In `test/Test/Services/FakePublishers.cs`, add `using Test.HostedServices;` if it is missing, then replace
```csharp
        public FakeMqttPublisher()
            : base(new MqttConnector(NullLogger<MqttConnector>.Instance, Options.Create(new MqttSettings())), NullLogger<MqttPublisher>.Instance)
```
with
```csharp
        /// <remarks>The connector wraps a FakeMqttClient, so no MQTTnet client (or socket) is ever created.</remarks>
        public FakeMqttPublisher()
            : base(new MqttConnector(NullLogger<MqttConnector>.Instance, Options.Create(new MqttSettings()), new FakeMqttClient()), NullLogger<MqttPublisher>.Instance)
```

- [ ] **Step 2: No real reconnect delay in MqttConnectorTests**

In `test/Test/HostedServices/MqttConnectorTests.cs`, replace
```csharp
            return new MqttConnector(
                NullLogger<MqttConnector>.Instance,
                Options.Create(settings ?? new MqttSettings()),
                client);
```
with
```csharp
            return new MqttConnector(
                NullLogger<MqttConnector>.Instance,
                Options.Create(settings ?? new MqttSettings()),
                client,
                // No real timers: a reconnect backoff waits until the connector is stopped or disposed.
                (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));
```
First check with `grep -n "ReconnectTask" test/Test/HostedServices/MqttConnectorTests.cs`. Expected: no matches. If a test there awaits `ReconnectTask`, give that one test its own connector built with the recording delay from `MqttConnectorReconnectTests.CreateConnector`, and report it.

- [ ] **Step 3: SlackStatusAppTests (only if G21 still lists `Apps/SlackStatus/SlackStatusAppTests.cs`)**

Replace its `new MqttConnector(<logger>, Options.Create(new MqttSettings()))` with `new MqttConnector(<same logger>, Options.Create(new MqttSettings()), new FakeMqttClient())`, and add `using Test.HostedServices;`.

- [ ] **Step 4: Verify**

```bash
grep -rn "new MqttConnector(" test/Test --include=*.cs
dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~MqttConnector|FullyQualifiedName~AwtrixServicePublishTests|FullyQualifiedName~MqttPublisher|FullyQualifiedName~SlackStatusApp"
dotnet test awtrix-sharp.sln -c Release 2>&1 | grep -E "Passed!|Failed!|error"
```
Expected:
- Every `new MqttConnector(` match passes a `client` or `new FakeMqttClient()` argument.
- The filtered tests pass.
- The full suite shows `Failed: 0`. `Test` Passed = Task 0 baseline + 13 (SlackConnector +2, AwtrixService null guards +2, Quantize rows +2, message ordering +1, TripSummary duration rows +3, un-skipped Factory test +1, Conductor timeout +1, AppNames.Configurable +1; the replaced Factory and timeout-constant tests are net 0). Skipped = baseline − 1. If WS5-WS7 removed a target and its Task step was skipped, subtract that step's tests.

- [ ] **Step 5: Commit**

```bash
git add test/Test/Services/FakePublishers.cs test/Test/HostedServices/MqttConnectorTests.cs
git commit -m "$(cat <<'EOF'
test(mqtt): fake client in FakeMqttPublisher; no real backoff timers in connector tests

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt
EOF
)"
```
(Append `test/Test/Apps/SlackStatus/SlackStatusAppTests.cs` to the `git add` only if Step 3 changed it.)

---

## Final verification

```bash
git status --short
grep -rn "UseHttpsRedirection\|SlackUserHarvester\|UserDnChanged\|SlackDndChangedEventArgs\|_slackApiClient\|Newtonsoft" src --include=*.cs
grep -rnE 'Log(Trace|Debug|Information|Warning|Error|Critical)\(\$"' src --include=*.cs
grep -c "8081" Dockerfile
dotnet test awtrix-sharp.sln -c Release 2>&1 | grep -E "Passed!|Failed!|error"
```
Expected:
- No WS8 files are left unstaged or uncommitted. Other agents' files may appear; leave them alone.
- Both greps print nothing, and `8081` count is `0`.
- Both test projects report `Failed: 0`.

## Spec coverage (self-review)

| Spec item | Task |
|---|---|
| D1 Dockerfile, D2 no-Docker simulation + optional `docker build`/inspect | 1 |
| D3 `UseHttpsRedirection`, D4 launchSettings, D5 harvester | 1 |
| D6/D7 SlackConnector instance fields, dead members, usings, DND doc, test | 2 |
| CR-42 ButtonApp logger (already fixed) | 0 (G11) |
| D8 `text` ordering, D9 Quantize | 3 |
| D10 MqttRender check, D11 AwtrixApp guard comment, D12 SlackUserId, TripTimer comment, Newtonsoft, controller block, residual logs | 5 |
| D13 duration, D14 Factory place + un-skip | 3 |
| D15 dispose timeout seam + test, D16 `AppNames.Configurable` | 4 |
| D17 FakeMqttPublisher / SlackStatusAppTests connectors, D18 no real delay | 6 |
| D19 AwtrixService null guards | 3 |
| Acceptance 1-10 | Tasks 1-6 + Final verification |
