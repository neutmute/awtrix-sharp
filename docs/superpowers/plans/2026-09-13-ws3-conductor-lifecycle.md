# WS3 Conductor and Hosted-Service Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The host always starts and stops cleanly:
- every app is created, then initialised exactly once, with failures isolated per app
- unknown types are skipped
- `ExecuteNow` drives the running instance and reports NotFound/Started/Error as 404/200/500
- shutdown awaits bounded async disposal

**Architecture:**
- `IAwtrixApp.InitAsync` replaces `Init`, and `ButtonApp` subscribes fire-and-forget over WS2's non-throwing `Subscribe`.
- `Conductor.StartAsync` runs three phases: create all apps, init all apps (each isolated), then bind buttons and register.
- A registry keyed by (device `BaseTopic`, `Type`) backs `FindApps`, `ExecuteNow` and `StopAsync`.
- `IAwtrixApp` gains `IAsyncDisposable`, which Conductor awaits with a per-app timeout.
- `SlackConnector.StopAsync` tolerates a missing client.

**Tech Stack:** .NET 10, ASP.NET Core, xUnit 2.9, Moq 4.20, MQTTnet 5.0.1.1416, NCrontab 3.3.3

**Spec:** `docs/superpowers/specs/2026-09-13-ws3-conductor-lifecycle-design.md`

## Global Constraints

- **Prerequisites:** WS1 and WS2 (`docs/superpowers/plans/2026-09-13-ws2-mqtt-connector.md`) are fully committed, and `git status --short` is clean before starting.
- **Config compatibility:** no `appsettings.json` keys or `AWTRIXSHARP_*` / `TRANSPORTOPENDATA__APIKEY` env vars are added, renamed or reinterpreted.
- **No new configuration.** Code constant: `Conductor.AppDisposeTimeout = TimeSpan.FromSeconds(10)`.
- **No package changes.** Target framework `net10.0`.
- **Routes and query parameters unchanged.** Start endpoints return 200 `{ message }` for Started, 404 `{ message }` for NotFound, and 500 `{ message }` for Error.
- **MQTT topics, HTTP device URLs and hosted-service registration order are unchanged.** Do not edit `Program.cs`.
- **Registry key comparison:** `StringComparison.Ordinal` for both `BaseTopic` and `Type`.
- **Out of scope:**
  - ScheduledApp state machine and dispose unification (WS4)
  - config validation (WS7)
  - SlackConnector static fields (WS8)
  - `MqttConnector`, `IMqttConnector`, `MqttRenderApp`, `MqttController`
- **No real sleeps in tests.** `WaitAsync(TimeSpan.FromSeconds(5))` is allowed only as a failure-mode guard.
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
- **Shell:** run every command from the repo root `C:\CodeMine\awtrix-sharp` in Git Bash. `sed -i` is used once, in Task 1.

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/api/Interfaces/IAwtrixApp.cs` | `InitAsync` (T1); `IAsyncDisposable` and lifecycle docs (T4) | 1, 4 |
| `src/api/Apps/AwtrixApp.cs` | `InitAsync` (T1); once-guard (T2); `DisposeAsync` (T4) | 1, 2, 4 |
| `src/api/Apps/Buttons/ButtonApp.cs` | non-blocking subscribe | 1 |
| `src/api/Apps/ScheduledApp.cs` | `DisposeAsync` override | 4 |
| `src/api/HostedServices/Conductor.cs` | interim `InitAsync` call sites (T1); three-phase start and registry (T2); `ExecuteNow` result (T3); bounded async stop (T4) | 1-4 |
| `src/api/HostedServices/AppExecutionResult.cs` | result enum | 3 |
| `src/api/Controllers/AppExecutionResponses.cs` | result → `IActionResult` mapping | 3 |
| `src/api/Controllers/MqttRenderController.cs`, `src/api/Controllers/TripTimerController.cs` | 404/200/500 | 3 |
| `src/api/HostedServices/SlackConnector.cs` | null-safe stop | 4 |
| `test/Test/HostedServices/ConductorTestHelper.cs` | extra optional mocks, `MockApp` | 2 |
| `test/Test/HostedServices/ConductorTests.cs` | factory and registry tests (rewritten T2, trimmed T3) | 2, 3 |
| `test/Test/HostedServices/ConductorStartupTests.cs` | CR-05/06/30 and button binding | 1, 2 |
| `test/Test/HostedServices/ConductorExecuteNowTests.cs` | CR-07 | 3 |
| `test/Test/HostedServices/ConductorShutdownTests.cs` | stop behaviour (T2 sync interim, T4 async) | 2, 4 |
| `test/Test/HostedServices/SlackConnectorTests.cs` | CR-16 | 4 |
| `test/Test/Apps/AwtrixAppTests.cs` | `InitAsync` (T1), once-guard (T2), `DisposeAsync` (T4) | 1, 2, 4 |
| `test/Test/Apps/Buttons/ButtonAppTests.cs` | non-blocking init | 1 |
| `test/Test/Apps/Diurnal/DiurnalAppTests.cs`, `test/Test/Apps/SlackStatus/SlackStatusAppTests.cs` | mechanical `InitAsync` rename | 1 |
| `test/Test/Apps/ScheduledAppTests.cs` | rename (T1); `DisposeAsync` test (T4) | 1, 4 |
| `test/Test/Apps/TripTimer/TripTimerControllerTests.cs` | registry seam (T2); 404/StartNow (T3) | 2, 3 |
| `test/Test/Controllers/MqttRenderControllerTests.cs` | rewritten for result mapping | 3 |

---

### Task 0: Verify WS1/WS2 interfaces as assumed

**Files:** none modified.

- [ ] **Step 1: Check the assumed shapes**

Run each command. The expected result follows each arrow.
- `git status --short` → empty. If it is not empty, stop: another agent is still working.
- `git log --oneline -12` → includes the WS2 commits:
  - `fix(mqtt): single long-lived client with handler and subscription registries`
  - `fix(mqtt): reconnect in the background with exponential backoff`
  - `fix(mqtt-render): attach message handler before subscribing`
  - `fix(api): /Mqtt/publish no longer reconnects the MQTT client`
  - WS1's `refactor(di): interface seams for Conductor and apps; validated composition root`
- `grep -n "Task Subscribe(string topic)" src/api/Interfaces/IMqttConnector.cs` → one match.
- `grep -n "_topics.Add(topic)" -B 8 src/api/HostedServices/MqttConnector.cs` → the registry add sits inside `Subscribe` **before** any `await`. D2 relies on this.
- `grep -n "Subscribe(buttonState.Topic).Wait()" src/api/Apps/Buttons/ButtonApp.cs` → one match (WS2 did not touch ButtonApp).
- `grep -n "void Init()" src/api/Interfaces/IAwtrixApp.cs src/api/Apps/AwtrixApp.cs` → one match in each.
- `grep -rn "IAwtrixApp" src test --include=*.cs | grep -v "/obj/" | grep "class "` → only `AwtrixApp<TConfig> : IAwtrixApp` implements it.
- `grep -n "protected Task FireAndLog" src/api/Apps/AwtrixApp.cs` → one match.
- `grep -n "public Conductor(" -A 11 src/api/HostedServices/Conductor.cs` → parameters `ILogger<Conductor>, IHostEnvironment, IOptions<AwtrixConfig>, ITimerService, ITripPlannerService, IAwtrixService, ISlackConnector, IMqttConnector, IClock, ILoggerFactory`.
- `grep -n "public static Conductor Create" -A 6 test/Test/HostedServices/ConductorTestHelper.cs` → parameters `config, hostEnvironment, awtrixService, mqttConnector, clock`.
- `grep -n "_slackSocketClient\|_executingTask" src/api/HostedServices/SlackConnector.cs` → `private static ISlackSocketModeClient _slackSocketClient;`, `private Task? _executingTask;` and `_slackSocketClient.Disconnect();`.
- `grep -rn "\.Init()" test --include=*.cs | grep -v "/obj/" | cut -d: -f1 | sort -u` → exactly:
  - `AwtrixAppTests.cs`
  - `ButtonAppTests.cs`
  - `DiurnalAppTests.cs`
  - `ScheduledAppTests.cs`
  - `SlackStatusAppTests.cs`

- [ ] **Step 2: Adapt if the landed code differs**

- **Different name:** if a name differs (for example `SubscribeAsync`, or a differently named helper parameter), use the landed name everywhere this plan uses the assumed one, and record the mapping in the Task 1 commit body.
- **`Subscribe` awaits before registering:** if `Subscribe` awaits before adding to its registry, keep D2 anyway (fire-and-forget is still observed by `FireAndLog`) and note it in the Task 1 commit body.
- **WS1/WS2 signatures:** do not change them.
- **Baseline:** run `dotnet test` and note the pass count (0 failed expected).

---

### Task 1: `InitAsync` and a non-blocking ButtonApp subscribe (CR-05, part 1)

**Files:**
- Modify: `src/api/Interfaces/IAwtrixApp.cs` (full replace)
- Modify: `src/api/Apps/AwtrixApp.cs` (replace `Init`)
- Modify: `src/api/Apps/Buttons/ButtonApp.cs` (replace `Initialize`)
- Modify: `src/api/HostedServices/Conductor.cs` (two interim call sites)
- Modify: `test/Test/Apps/AwtrixAppTests.cs` (two tests)
- Modify: `test/Test/Apps/Buttons/ButtonAppTests.cs` (full replace)
- Modify: `test/Test/Apps/Diurnal/DiurnalAppTests.cs`, `test/Test/Apps/ScheduledAppTests.cs`, `test/Test/Apps/SlackStatus/SlackStatusAppTests.cs` (mechanical rename)
- Create: `test/Test/HostedServices/ConductorStartupTests.cs`

**Interfaces:**
- Consumes: `IMqttConnector.Subscribe(string) : Task` (WS2, never throws); `AwtrixApp.FireAndLog(Func<Task>, string) : Task` (WS1).
- Produces:
  - `IAwtrixApp.InitAsync() : Task`
  - `AwtrixApp<TConfig>.InitAsync() : Task` (public, non-virtual)
  - `ConductorStartupTests` class (extended in Task 2)

- [ ] **Step 1: Write the failing tests**

Replace the whole of `test/Test/Apps/Buttons/ButtonAppTests.cs`:

```csharp
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Test.Apps.MqttRender;

namespace Test.Apps.Buttons
{
    public class ButtonAppTests
    {
        private Mock<ILogger> _mockLogger = null!;
        private Mock<IAwtrixService> _mockAwtrixService = null!;
        private Mock<IMqttConnector> _mockMqttConnector = null!;
        private AwtrixAddress _address = null!;

        private ButtonApp CreateSut()
        {
            _mockLogger = new Mock<ILogger>();
            _mockAwtrixService = new Mock<IAwtrixService>();
            _mockMqttConnector = new Mock<IMqttConnector>();
            _address = new AwtrixAddress { BaseTopic = "test/base/topic" };

            _mockMqttConnector.Setup(x => x.Subscribe(It.IsAny<string>())).Returns(Task.CompletedTask);

            var config = new AppConfig();

            return new ButtonApp(_mockLogger.Object, config, _address, _mockAwtrixService.Object, _mockMqttConnector.Object);
        }

        [Fact]
        public async Task InitAsync_SubscribesToAllThreeButtonTopics()
        {
            var sut = CreateSut();

            await sut.InitAsync();

            _mockMqttConnector.Verify(x => x.Subscribe("test/base/topic/stats/buttonLeft"), Times.Once);
            _mockMqttConnector.Verify(x => x.Subscribe("test/base/topic/stats/buttonRight"), Times.Once);
            _mockMqttConnector.Verify(x => x.Subscribe("test/base/topic/stats/buttonSelect"), Times.Once);
        }

        [Fact]
        public async Task InitAsync_WhenSubscribeNeverCompletes_DoesNotBlock_AndStillRaisesClick()
        {
            // CR-05: a hung SUBSCRIBE (half-open broker connection) must not stall host startup
            var sut = CreateSut();
            _mockMqttConnector.Setup(x => x.Subscribe(It.IsAny<string>())).Returns(new TaskCompletionSource().Task);

            await Task.Run(() => sut.InitAsync()).WaitAsync(TimeSpan.FromSeconds(5));

            ButtonEventArgs? received = null;
            sut.Click += (s, e) => received = e;
            _mockMqttConnector.Raise(x => x.MessageReceived += null,
                new object[] { MqttTestHelpers.CreateReceivedArgs("test/base/topic/stats/buttonRight", "1") });

            Assert.NotNull(received);
            Assert.Equal(Button.Right, received!.Button);
            _mockMqttConnector.Verify(x => x.Subscribe("test/base/topic/stats/buttonRight"), Times.Once);
        }

        [Fact]
        public async Task MessageReceived_ButtonPressed_RaisesClick()
        {
            var sut = CreateSut();
            await sut.InitAsync();
            ButtonEventArgs? received = null;
            sut.Click += (s, e) => received = e;

            var args = MqttTestHelpers.CreateReceivedArgs("test/base/topic/stats/buttonLeft", "1");
            _mockMqttConnector.Raise(x => x.MessageReceived += null, new object[] { args });

            Assert.NotNull(received);
            Assert.Equal(Button.Left, received!.Button);
        }

        [Fact]
        public async Task MessageReceived_UnrelatedTopic_DoesNotRaiseClick()
        {
            var sut = CreateSut();
            await sut.InitAsync();
            var clickCount = 0;
            sut.Click += (s, e) => clickCount++;

            var args = MqttTestHelpers.CreateReceivedArgs("some/other/topic", "1");
            _mockMqttConnector.Raise(x => x.MessageReceived += null, new object[] { args });

            Assert.Equal(0, clickCount);
        }

        [Fact]
        public async Task MessageReceived_PressReleasePressQuickly_RaisesDoubleClick()
        {
            var sut = CreateSut();
            await sut.InitAsync();
            var clickCount = 0;
            var doubleClickCount = 0;
            sut.Click += (s, e) => clickCount++;
            sut.DoubleClick += (s, e) => doubleClickCount++;

            var topic = "test/base/topic/stats/buttonSelect";
            _mockMqttConnector.Raise(x => x.MessageReceived += null, new object[] { MqttTestHelpers.CreateReceivedArgs(topic, "1") });
            _mockMqttConnector.Raise(x => x.MessageReceived += null, new object[] { MqttTestHelpers.CreateReceivedArgs(topic, "0") });
            _mockMqttConnector.Raise(x => x.MessageReceived += null, new object[] { MqttTestHelpers.CreateReceivedArgs(topic, "1") });

            Assert.Equal(1, clickCount);
            Assert.Equal(1, doubleClickCount);
        }
    }
}
```

In `test/Test/Apps/AwtrixAppTests.cs`, replace the two tests `Init_WithNamedConfig_ClearsAppAndCallsInitialize` and `Init_WithNullName_SkipsAppClear_ButStillInitializes` with:

```csharp
        [Fact]
        public async Task InitAsync_WithNamedConfig_ClearsAppAndCallsInitialize()
        {
            var sut = CreateSut("MyApp");

            await sut.InitAsync();

            _mockAwtrixService.Verify(x => x.AppClear(_address, "MyApp"), Times.Once);
            Assert.Equal(1, sut.InitializeCallCount);
        }

        [Fact]
        public async Task InitAsync_WithNullName_SkipsAppClear_ButStillInitializes()
        {
            var sut = CreateSut(type: null);

            await sut.InitAsync();

            _mockAwtrixService.Verify(x => x.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>()), Times.Never);
            Assert.Equal(1, sut.InitializeCallCount);
        }
```

Create `test/Test/HostedServices/ConductorStartupTests.cs`:

```csharp
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using Moq;
using Test.Apps;

namespace Test.HostedServices
{
    /// <summary>
    /// Conductor.StartAsync over interface mocks: startup must never block or throw because of one app,
    /// the broker, or a device (CR-05, CR-06, CR-30).
    /// </summary>
    public class ConductorStartupTests
    {
        private static readonly DateTimeOffset HalfPastMidnight = new DateTimeOffset(2026, 9, 13, 0, 30, 0, TimeSpan.FromHours(10));

        [Fact]
        public async Task StartAsync_WhenMqttSubscribeNeverCompletes_StillCompletes()
        {
            var device = new DeviceConfig { BaseTopic = "awtrix/clock1" };
            var mqtt = new Mock<IMqttConnector>();
            mqtt.Setup(m => m.Subscribe(It.IsAny<string>())).Returns(new TaskCompletionSource().Task);
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                mqttConnector: mqtt.Object,
                clock: new MockClock(HalfPastMidnight));

            await Task.Run(() => conductor.StartAsync(CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Single(conductor.FindApps(AppNames.ButtonApp));
        }
    }
}
```

Apply the mechanical rename to the remaining `Init()` call sites:

```bash
sed -i 's/\.Init()/.InitAsync().GetAwaiter().GetResult()/g' test/Test/Apps/Diurnal/DiurnalAppTests.cs test/Test/Apps/ScheduledAppTests.cs test/Test/Apps/SlackStatus/SlackStatusAppTests.cs
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps.Buttons.ButtonAppTests|FullyQualifiedName~Test.Apps.AwtrixAppTests|FullyQualifiedName~Test.HostedServices.ConductorStartupTests"`
Expected: build FAILS with `CS1061: 'ButtonApp' does not contain a definition for 'InitAsync'`, and the same for `TestAwtrixApp`, `DiurnalApp`, etc.

- [ ] **Step 3: Implement**

Replace the whole of `src/api/Interfaces/IAwtrixApp.cs`:

```csharp
using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Interfaces
{
    public interface IAwtrixApp : IDisposable
    {
        public AwtrixAddress AwtrixAddress { get; }

        public IAppConfig GetConfig();

        /// <summary>
        /// Clears the app's slot and wires its subscriptions/schedule. Must not block on the network
        /// beyond the publisher timeouts.
        /// </summary>
        Task InitAsync();

        void ExecuteNow();
    }
}
```

In `src/api/Apps/AwtrixApp.cs`, replace:

```csharp
        public void Init()
        {
            _ = AppClear().Result;

            Logger.LogInformation("Initializing {Config} for {AwtrixAddress}", Config.Type, AwtrixAddress);

            Initialize();
        }
```

with:

```csharp
        public async Task InitAsync()
        {
            await AppClear();

            Logger.LogInformation("Initializing {Config} for {AwtrixAddress}", Config.Type, AwtrixAddress);

            Initialize();
        }
```

In `src/api/Apps/Buttons/ButtonApp.cs`, replace the `Initialize` method with:

```csharp
        protected override void Initialize()
        {
            foreach (var buttonState in _buttonTopics.Values)
            {
                // Subscribe records the topic and never throws (MqttConnector); don't block startup waiting for SUBACK
                var topic = buttonState.Topic;
                _ = FireAndLog(() => _mqttConnector.Subscribe(topic), $"Subscribe {topic}");
                buttonState.Click += (s, e) => Click?.Invoke(this, e);
                buttonState.DoubleClick += (s, e) => DoubleClick?.Invoke(this, e);
            }
            _mqttConnector.MessageReceived += RawMessageReceived;
        }
```

In `src/api/HostedServices/Conductor.cs`, make these interim edits (Task 2 replaces `StartAsync`; Task 3 replaces `ExecuteNow`):
1. `public Task StartAsync(CancellationToken cancellationToken)` → `public async Task StartAsync(CancellationToken cancellationToken)`
2. Replace:

```csharp
                foreach (var app in _apps)
                {
                    app.Init();
                }
            }

            return Task.CompletedTask;
        }
```

with:

```csharp
                foreach (var app in _apps)
                {
                    await app.InitAsync();
                }
            }
        }
```

3. In `ExecuteNow`, replace:

```csharp
                var app = AppFactory(device, config);
                app.Init();
```

with:

```csharp
                var app = AppFactory(device, config);
                app.InitAsync().GetAwaiter().GetResult();
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps.Buttons.ButtonAppTests|FullyQualifiedName~Test.Apps.AwtrixAppTests|FullyQualifiedName~Test.HostedServices.ConductorStartupTests"`
Expected: PASS, 0 failed.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: 0 failed. `grep -rn "\.Init()" src test --include=*.cs | grep -v "/obj/"` → no matches.

- [ ] **Step 6: Commit**

```bash
git add src/api/Interfaces/IAwtrixApp.cs src/api/Apps/AwtrixApp.cs src/api/Apps/Buttons/ButtonApp.cs src/api/HostedServices/Conductor.cs test/Test/Apps/AwtrixAppTests.cs test/Test/Apps/Buttons/ButtonAppTests.cs test/Test/Apps/Diurnal/DiurnalAppTests.cs test/Test/Apps/ScheduledAppTests.cs test/Test/Apps/SlackStatus/SlackStatusAppTests.cs test/Test/HostedServices/ConductorStartupTests.cs
git commit -m "fix(apps): async InitAsync and non-blocking ButtonApp subscribe (CR-05)

IAwtrixApp.Init() becomes Task InitAsync(); AwtrixApp awaits AppClear instead of .Result.
ButtonApp issues its subscriptions via FireAndLog instead of Subscribe(...).Wait().

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 2: Three-phase startup, init once, per-app isolation, registry (CR-05, CR-06, CR-30)

**Files:**
- Modify: `src/api/HostedServices/Conductor.cs` (full replace)
- Modify: `src/api/Apps/AwtrixApp.cs` (once-guard)
- Modify: `test/Test/HostedServices/ConductorTestHelper.cs` (full replace)
- Modify: `test/Test/HostedServices/ConductorTests.cs` (full replace)
- Modify: `test/Test/HostedServices/ConductorStartupTests.cs` (full replace)
- Create: `test/Test/HostedServices/ConductorShutdownTests.cs`
- Modify: `test/Test/Apps/AwtrixAppTests.cs` (`TestAwtrixApp` and two new tests)
- Modify: `test/Test/Apps/TripTimer/TripTimerControllerTests.cs` (helper only)

**Interfaces:**
- Consumes: `IAwtrixApp.InitAsync()` (Task 1).
- Produces:
  - `Conductor.FindApps(string appType, string? baseTopic = null) : List<IAwtrixApp>`
  - `internal void Conductor.RegisterApp(IAwtrixApp app)`
  - `private sealed record Conductor.RegisteredApp(AwtrixAddress? Device, string Type, IAwtrixApp App)` with `string BaseTopic`
  - `private Task Conductor.DisposeOneAsync(RegisteredApp entry, CancellationToken cancellationToken)` (body replaced in Task 4)
  - `private static bool Conductor.Matches(RegisteredApp entry, string appType, string? baseTopic)`
  - `internal static readonly string[] AppNames.All`
  - `ConductorTestHelper.Create(config, hostEnvironment, awtrixService, mqttConnector, clock, timerService, tripPlanner, slackConnector)`
  - `ConductorTestHelper.MockApp(string baseTopic, string type) : Mock<IAwtrixApp>`

- [ ] **Step 1: Write the failing tests**

Replace the whole of `test/Test/HostedServices/ConductorTestHelper.cs`:

```csharp
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Test.HostedServices
{
    /// <summary>
    /// Builds a Conductor over interface mocks. Moq's defaults return completed tasks
    /// (Task&lt;bool&gt; => false), so StartAsync can run without a broker or network.
    /// </summary>
    internal static class ConductorTestHelper
    {
        public static Conductor Create(
            AwtrixConfig? config = null,
            IHostEnvironment? hostEnvironment = null,
            IAwtrixService? awtrixService = null,
            IMqttConnector? mqttConnector = null,
            IClock? clock = null,
            ITimerService? timerService = null,
            ITripPlannerService? tripPlanner = null,
            ISlackConnector? slackConnector = null)
        {
            config ??= new AwtrixConfig { Devices = Array.Empty<DeviceConfig>() };

            var env = hostEnvironment;
            if (env == null)
            {
                var envMock = new Mock<IHostEnvironment>();
                envMock.Setup(e => e.EnvironmentName).Returns("Production");
                env = envMock.Object;
            }

            return new Conductor(
                NullLogger<Conductor>.Instance,
                env,
                Options.Create(config),
                timerService ?? new Mock<ITimerService>().Object,
                tripPlanner ?? new Mock<ITripPlannerService>().Object,
                awtrixService ?? new Mock<IAwtrixService>().Object,
                slackConnector ?? new Mock<ISlackConnector>().Object,
                mqttConnector ?? new Mock<IMqttConnector>().Object,
                clock ?? new Clock(),
                NullLoggerFactory.Instance);
        }

        /// <summary>
        /// An app mock that reports the device and Type the Conductor registry keys on.
        /// </summary>
        public static Mock<IAwtrixApp> MockApp(string baseTopic, string type)
        {
            var app = new Mock<IAwtrixApp>();
            app.Setup(a => a.AwtrixAddress).Returns(new AwtrixAddress { BaseTopic = baseTopic });
            app.Setup(a => a.GetConfig()).Returns(AppConfig.Empty().WithName(type));
            return app;
        }
    }
}
```

Replace the whole of `test/Test/HostedServices/ConductorTests.cs`:

```csharp
using System.Reflection;
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.Diurnal;
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Apps.SlackStatus;
using AwtrixSharpWeb.Apps.TripTimer;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Test.Apps;
using Test.Services;

namespace Test.HostedServices
{
    /// <summary>
    /// Covers Conductor's app factory switch, the registry (FindApps) and the StartAsync basics.
    /// Startup isolation: ConductorStartupTests. ExecuteNow: ConductorExecuteNowTests. Stop: ConductorShutdownTests.
    /// </summary>
    public class ConductorTests
    {
        private static IAwtrixApp? InvokeAppFactory(Conductor conductor, DeviceConfig device, AppConfig appConfig)
        {
            var method = typeof(Conductor).GetMethod("AppFactory", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            try
            {
                return (IAwtrixApp?)method!.Invoke(conductor, new object[] { device, appConfig });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        private static DeviceConfig CreateDevice() => new DeviceConfig { BaseTopic = "awtrix/clock1" };

        private static readonly DateTimeOffset HalfPastMidnight = new DateTimeOffset(2026, 9, 13, 0, 30, 0, TimeSpan.FromHours(10));

        [Fact]
        public void AppFactory_DiurnalApp_CreatesDiurnalApp()
        {
            var app = InvokeAppFactory(ConductorTestHelper.Create(), CreateDevice(), AppConfig.Empty().WithName(AppNames.DiurnalApp));

            Assert.IsType<DiurnalApp>(app);
        }

        [Fact]
        public void AppFactory_ButtonApp_CreatesButtonApp()
        {
            var app = InvokeAppFactory(ConductorTestHelper.Create(), CreateDevice(), AppConfig.Empty().WithName(AppNames.ButtonApp));

            Assert.IsType<ButtonApp>(app);
        }

        [Fact]
        public void AppFactory_MqttRenderApp_CreatesMqttRenderApp()
        {
            var app = InvokeAppFactory(ConductorTestHelper.Create(), CreateDevice(), AppConfig.Empty().WithName(AppNames.MqttRenderApp));

            Assert.IsType<MqttRenderApp>(app);
        }

        [Fact]
        public void AppFactory_MqttClockRenderApp_CreatesMqttClockRenderApp()
        {
            var app = InvokeAppFactory(ConductorTestHelper.Create(), CreateDevice(), AppConfig.Empty().WithName(AppNames.MqttClockRenderApp));

            Assert.IsType<MqttClockRenderApp>(app);
        }

        [Fact]
        public void AppFactory_TripTimerApp_CreatesTripTimerApp()
        {
            var app = InvokeAppFactory(ConductorTestHelper.Create(), CreateDevice(), AppConfig.Empty().WithName(AppNames.TripTimerApp));

            Assert.IsType<TripTimerApp>(app);
        }

        [Fact]
        public void AppFactory_SlackStatusApp_CreatesSlackStatusApp()
        {
            var app = InvokeAppFactory(ConductorTestHelper.Create(), CreateDevice(), AppConfig.Empty().WithName(AppNames.SlackStatusApp));

            Assert.IsType<SlackStatusApp>(app);
        }

        [Fact]
        public void AppFactory_UnknownType_ReturnsNullWithoutThrowing()
        {
            // CR-30: a typo in Type is logged and skipped, not fatal
            var app = InvokeAppFactory(ConductorTestHelper.Create(), CreateDevice(), AppConfig.Empty().WithName("SomeUnknownAppType"));

            Assert.Null(app);
        }

        [Fact]
        public void AppFactory_CreatedApp_HasExpectedAwtrixAddress()
        {
            var app = InvokeAppFactory(ConductorTestHelper.Create(), CreateDevice(), AppConfig.Empty().WithName(AppNames.DiurnalApp));

            Assert.Equal("awtrix/clock1", app!.AwtrixAddress.BaseTopic);
        }

        [Fact]
        public void AppNamesAll_ListsEveryFactoryType()
        {
            Assert.Equal(
                new[] { AppNames.DiurnalApp, AppNames.ButtonApp, AppNames.TripTimerApp, AppNames.SlackStatusApp, AppNames.MqttRenderApp, AppNames.MqttClockRenderApp },
                AppNames.All);
        }

        [Fact]
        public void ExecuteNow_UnknownDevice_DoesNotThrow()
        {
            var conductor = ConductorTestHelper.Create(new AwtrixConfig { Devices = new[] { CreateDevice() } });

            var exception = Record.Exception(() => conductor.ExecuteNow("awtrix/does-not-exist", AppNames.DiurnalApp));

            Assert.Null(exception);
        }

        [Fact]
        public void ExecuteNow_UnknownAppOnKnownDevice_DoesNotThrow()
        {
            var device = CreateDevice();
            var conductor = ConductorTestHelper.Create(new AwtrixConfig { Devices = new[] { device } });

            var exception = Record.Exception(() => conductor.ExecuteNow(device.BaseTopic, "NoSuchApp"));

            Assert.Null(exception);
        }

        [Fact]
        public void FindApps_WhenNoAppsRegistered_ReturnsEmptyList()
        {
            var conductor = ConductorTestHelper.Create();

            Assert.Empty(conductor.FindApps(AppNames.DiurnalApp));
        }

        [Fact]
        public void FindApps_FiltersByConfigType()
        {
            var conductor = ConductorTestHelper.Create();
            var matching = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.DiurnalApp);
            var nonMatching = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.MqttRenderApp);
            conductor.RegisterApp(matching.Object);
            conductor.RegisterApp(nonMatching.Object);

            var result = conductor.FindApps(AppNames.DiurnalApp);

            Assert.Single(result);
            Assert.Same(matching.Object, result[0]);
        }

        [Fact]
        public void FindApps_WithBaseTopic_ReturnsOnlyThatDevicesApps()
        {
            var conductor = ConductorTestHelper.Create();
            var clock1 = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.TripTimerApp);
            var clock2 = ConductorTestHelper.MockApp("awtrix/clock2", AppNames.TripTimerApp);
            conductor.RegisterApp(clock1.Object);
            conductor.RegisterApp(clock2.Object);

            Assert.Same(clock2.Object, Assert.Single(conductor.FindApps(AppNames.TripTimerApp, "awtrix/clock2")));
            Assert.Equal(2, conductor.FindApps(AppNames.TripTimerApp).Count);
            Assert.Empty(conductor.FindApps(AppNames.TripTimerApp, "AWTRIX/CLOCK2"));
        }

        [Fact]
        public async Task StartAsync_HttpDevice_DoesNotCreateButtonApp()
        {
            var device = new DeviceConfig { BaseTopic = "http://192.168.1.50/api" };
            device.Apps.Add(AppConfig.Empty().WithName(AppNames.DiurnalApp));
            var awtrix = new Mock<IAwtrixService>();
            var mqtt = new Mock<IMqttConnector>();
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                awtrixService: awtrix.Object,
                mqttConnector: mqtt.Object,
                clock: new MockClock(HalfPastMidnight));

            await conductor.StartAsync(CancellationToken.None);

            Assert.Empty(conductor.FindApps(AppNames.ButtonApp));
            Assert.Single(conductor.FindApps(AppNames.DiurnalApp));
            mqtt.Verify(m => m.Subscribe(It.IsAny<string>()), Times.Never);
            awtrix.Verify(a => a.AppClear(device, AppNames.DiurnalApp), Times.Once);
        }

        [Fact]
        public async Task StartAsync_MqttDevice_CreatesButtonAppAndSubscribesToButtons()
        {
            var device = new DeviceConfig { BaseTopic = "awtrix/clock1" };
            var mqtt = new Mock<IMqttConnector>();
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                mqttConnector: mqtt.Object,
                clock: new MockClock(HalfPastMidnight));

            await conductor.StartAsync(CancellationToken.None);

            Assert.Single(conductor.FindApps(AppNames.ButtonApp, "awtrix/clock1"));
            mqtt.Verify(m => m.Subscribe("awtrix/clock1/stats/buttonLeft"), Times.Once);
            mqtt.Verify(m => m.Subscribe("awtrix/clock1/stats/buttonSelect"), Times.Once);
            mqtt.Verify(m => m.Subscribe("awtrix/clock1/stats/buttonRight"), Times.Once);
        }

        [Fact]
        public async Task StartAsync_WithUnreachableHttpDevice_Completes()
        {
            // CR-02 scenario: device unplugged at startup must not fail host start
            var offline = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("No route to host"));
            var httpPublisher = new HttpPublisher(NullLogger<HttpPublisher>.Instance, new StubHttpClientFactory(offline));
            var awtrixService = new AwtrixService(httpPublisher, new FakeMqttPublisher());

            var device = new DeviceConfig { BaseTopic = "http://192.168.1.50/api" };
            var diurnal = AppConfig.Empty().WithName(AppNames.DiurnalApp);
            diurnal.Config["0000"] = "Brightness=8"; // before "now", so startup replay publishes too
            device.Apps.Add(diurnal);

            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                awtrixService: awtrixService,
                clock: new MockClock(HalfPastMidnight));

            var exception = await Record.ExceptionAsync(() => conductor.StartAsync(CancellationToken.None));

            Assert.Null(exception);
            Assert.Single(conductor.FindApps(AppNames.DiurnalApp));
            Assert.NotEmpty(offline.Requests);
        }
    }
}
```

Replace the whole of `test/Test/HostedServices/ConductorStartupTests.cs`:

```csharp
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Services.TripPlanner;
using Moq;
using Test.Apps;
using Test.Apps.MqttRender;

namespace Test.HostedServices
{
    /// <summary>
    /// Conductor.StartAsync over interface mocks: startup must never block or throw because of one app,
    /// the broker, or a device (CR-05, CR-06, CR-30).
    /// </summary>
    public class ConductorStartupTests
    {
        private static readonly DateTimeOffset HalfPastMidnight = new DateTimeOffset(2026, 9, 13, 0, 30, 0, TimeSpan.FromHours(10));

        private const string HttpDevice = "http://192.168.1.50/api";

        private static DeviceConfig Device(string baseTopic, params AppConfig[] apps)
        {
            var device = new DeviceConfig { BaseTopic = baseTopic };
            device.Apps.AddRange(apps);
            return device;
        }

        private static AppConfig App(string type) => AppConfig.Empty().WithName(type);

        private static AppConfig TripTimer()
        {
            var config = App(AppNames.TripTimerApp);
            config.Config["CronSchedule"] = "0 6 * * 1-5";
            config.Config["ActiveTime"] = "00:30:00";
            config.Config["TimeToOrigin"] = "00:10:00";
            config.Config["TimeToPrepare"] = "00:05:00";
            config.Config["StopIdOrigin"] = "1";
            config.Config["StopIdDestination"] = "2";
            return config;
        }

        private static void RaiseDoubleClick(Mock<IMqttConnector> mqtt, string topic)
        {
            foreach (var payload in new[] { "1", "0", "1" })
            {
                mqtt.Raise(m => m.MessageReceived += null, new object[] { MqttTestHelpers.CreateReceivedArgs(topic, payload) });
            }
        }

        [Fact]
        public async Task StartAsync_WhenMqttSubscribeNeverCompletes_StillCompletes()
        {
            var device = new DeviceConfig { BaseTopic = "awtrix/clock1" };
            var mqtt = new Mock<IMqttConnector>();
            mqtt.Setup(m => m.Subscribe(It.IsAny<string>())).Returns(new TaskCompletionSource().Task);
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                mqttConnector: mqtt.Object,
                clock: new MockClock(HalfPastMidnight));

            await Task.Run(() => conductor.StartAsync(CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Single(conductor.FindApps(AppNames.ButtonApp));
        }

        [Fact]
        public async Task StartAsync_TwoDevices_InitialisesEachAppExactlyOnce()
        {
            // CR-06: device 1's apps used to be re-initialised once per later device
            var clock1 = Device("awtrix/clock1", App(AppNames.DiurnalApp));
            var clock2 = Device("awtrix/clock2", App(AppNames.DiurnalApp));
            var awtrix = new Mock<IAwtrixService>();
            var mqtt = new Mock<IMqttConnector>();
            var timer = new Mock<ITimerService>();
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { clock1, clock2 } },
                awtrixService: awtrix.Object,
                mqttConnector: mqtt.Object,
                clock: new MockClock(HalfPastMidnight),
                timerService: timer.Object);

            await conductor.StartAsync(CancellationToken.None);

            awtrix.Verify(a => a.AppClear(clock1, AppNames.DiurnalApp), Times.Once);
            awtrix.Verify(a => a.AppClear(clock1, AppNames.ButtonApp), Times.Once);
            awtrix.Verify(a => a.AppClear(clock2, AppNames.DiurnalApp), Times.Once);
            mqtt.Verify(m => m.Subscribe("awtrix/clock1/stats/buttonLeft"), Times.Once);
            timer.VerifyAdd(t => t.MinuteChanged += It.IsAny<EventHandler<ClockTickEventArgs>>(), Times.Exactly(2));
        }

        [Fact]
        public async Task StartAsync_CalledTwice_DoesNotCreateOrInitialiseAgain()
        {
            var device = Device(HttpDevice, App(AppNames.DiurnalApp));
            var awtrix = new Mock<IAwtrixService>();
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                awtrixService: awtrix.Object,
                clock: new MockClock(HalfPastMidnight));

            await conductor.StartAsync(CancellationToken.None);
            await conductor.StartAsync(CancellationToken.None);

            awtrix.Verify(a => a.AppClear(device, AppNames.DiurnalApp), Times.Once);
            Assert.Single(conductor.FindApps(AppNames.DiurnalApp));
        }

        [Fact]
        public async Task StartAsync_WhenOneAppInitThrows_StartsTheOthers()
        {
            // CR-05: one bad app must not kill every device
            var device = Device(HttpDevice, App(AppNames.DiurnalApp), App(AppNames.SlackStatusApp));
            var awtrix = new Mock<IAwtrixService>();
            awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), AppNames.DiurnalApp))
                .ThrowsAsync(new InvalidOperationException("boom"));
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                awtrixService: awtrix.Object,
                clock: new MockClock(HalfPastMidnight));

            var exception = await Record.ExceptionAsync(() => conductor.StartAsync(CancellationToken.None));

            Assert.Null(exception);
            Assert.Empty(conductor.FindApps(AppNames.DiurnalApp));
            Assert.Single(conductor.FindApps(AppNames.SlackStatusApp));
        }

        [Fact]
        public async Task StartAsync_UnknownAppType_IsSkippedAndOtherAppsStart()
        {
            // CR-30
            var device = Device(HttpDevice, App("MqttRendrApp"), App(AppNames.DiurnalApp));
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                clock: new MockClock(HalfPastMidnight));

            var exception = await Record.ExceptionAsync(() => conductor.StartAsync(CancellationToken.None));

            Assert.Null(exception);
            Assert.Empty(conductor.FindApps("MqttRendrApp"));
            Assert.Single(conductor.FindApps(AppNames.DiurnalApp));
        }

        [Fact]
        public async Task StartAsync_AppWithNoType_IsSkippedAndOtherAppsStart()
        {
            var device = Device(HttpDevice, new AppConfig(), App(AppNames.DiurnalApp));
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                clock: new MockClock(HalfPastMidnight));

            var exception = await Record.ExceptionAsync(() => conductor.StartAsync(CancellationToken.None));

            Assert.Null(exception);
            Assert.Single(conductor.FindApps(AppNames.DiurnalApp));
        }

        [Fact]
        public async Task StartAsync_ScheduledAppWithoutCronSchedule_IsSkippedAndDisposed()
        {
            // CR-30: CrontabSchedule.Parse(null) used to fail host start
            var device = Device(HttpDevice, App(AppNames.MqttRenderApp), App(AppNames.DiurnalApp));
            var awtrix = new Mock<IAwtrixService>();
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                awtrixService: awtrix.Object,
                clock: new MockClock(HalfPastMidnight));

            var exception = await Record.ExceptionAsync(() => conductor.StartAsync(CancellationToken.None));

            Assert.Null(exception);
            Assert.Empty(conductor.FindApps(AppNames.MqttRenderApp));
            Assert.Single(conductor.FindApps(AppNames.DiurnalApp));
            awtrix.Verify(a => a.Dismiss(device), Times.Once);
        }

        [Fact]
        public async Task StartAsync_RightDoubleClick_StartsOnlyThatDevicesTripTimerOnce()
        {
            var clock1 = Device("awtrix/clock1", TripTimer());
            var clock2 = Device("awtrix/clock2", TripTimer());
            var awtrix = new Mock<IAwtrixService>();
            var mqtt = new Mock<IMqttConnector>();
            var tripPlanner = new Mock<ITripPlannerService>();
            tripPlanner
                .Setup(t => t.GetNextDepartures(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new List<TripSummary>());
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { clock1, clock2 } },
                awtrixService: awtrix.Object,
                mqttConnector: mqtt.Object,
                clock: new MockClock(HalfPastMidnight),
                tripPlanner: tripPlanner.Object);
            await conductor.StartAsync(CancellationToken.None);

            RaiseDoubleClick(mqtt, "awtrix/clock1/stats/buttonRight");

            // TripTimerApp's activation announces itself with one Notify ("Starting trip timer")
            awtrix.Verify(a => a.Notify(clock1, It.IsAny<AwtrixAppMessage>()), Times.Once);
            awtrix.Verify(a => a.Notify(clock2, It.IsAny<AwtrixAppMessage>()), Times.Never);

            await conductor.StopAsync(CancellationToken.None);
        }
    }
}
```

Create `test/Test/HostedServices/ConductorShutdownTests.cs` (interim; Task 4 replaces it):

```csharp
using AwtrixSharpWeb.HostedServices;
using Moq;

namespace Test.HostedServices
{
    public class ConductorShutdownTests
    {
        [Fact]
        public async Task StopAsync_WithNoRegisteredApps_CompletesWithoutError()
        {
            var conductor = ConductorTestHelper.Create();

            var exception = await Record.ExceptionAsync(() => conductor.StopAsync(CancellationToken.None));

            Assert.Null(exception);
        }

        [Fact]
        public async Task StopAsync_DisposesAllRegisteredApps()
        {
            var conductor = ConductorTestHelper.Create();
            var app1 = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.DiurnalApp);
            var app2 = ConductorTestHelper.MockApp("awtrix/clock2", AppNames.DiurnalApp);
            conductor.RegisterApp(app1.Object);
            conductor.RegisterApp(app2.Object);

            await conductor.StopAsync(CancellationToken.None);

            app1.Verify(a => a.Dispose(), Times.Once);
            app2.Verify(a => a.Dispose(), Times.Once);
        }

        [Fact]
        public async Task StopAsync_WhenOneAppDisposeThrows_StillDisposesOthers()
        {
            var conductor = ConductorTestHelper.Create();
            var throwing = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.DiurnalApp);
            throwing.Setup(a => a.Dispose()).Throws(new AggregateException(new HttpRequestException("offline")));
            var healthy = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.SlackStatusApp);
            conductor.RegisterApp(throwing.Object);
            conductor.RegisterApp(healthy.Object);

            var exception = await Record.ExceptionAsync(() => conductor.StopAsync(CancellationToken.None));

            Assert.Null(exception);
            healthy.Verify(a => a.Dispose(), Times.Once);
        }

        [Fact]
        public async Task StopAsync_ClearsTheRegistry()
        {
            var conductor = ConductorTestHelper.Create();
            conductor.RegisterApp(ConductorTestHelper.MockApp("awtrix/clock1", AppNames.DiurnalApp).Object);

            await conductor.StopAsync(CancellationToken.None);

            Assert.Empty(conductor.FindApps(AppNames.DiurnalApp));
        }
    }
}
```

In `test/Test/Apps/AwtrixAppTests.cs`, replace the `TestAwtrixApp` members:

```csharp
        public int InitializeCallCount { get; private set; }
```

and

```csharp
        protected override void Initialize()
        {
            InitializeCallCount++;
        }
```

with:

```csharp
        public int InitializeCallCount { get; private set; }

        public Exception? InitializeException { get; set; }
```

and

```csharp
        protected override void Initialize()
        {
            InitializeCallCount++;
            if (InitializeException != null)
            {
                throw InitializeException;
            }
        }
```

Then add these tests inside `public class AwtrixAppTests`, after `InitAsync_WithNullName_SkipsAppClear_ButStillInitializes`:

```csharp
        [Fact]
        public async Task InitAsync_CalledTwice_ClearsAndInitializesOnce()
        {
            var sut = CreateSut("MyApp");

            await sut.InitAsync();
            await sut.InitAsync();

            _mockAwtrixService.Verify(x => x.AppClear(_address, "MyApp"), Times.Once);
            Assert.Equal(1, sut.InitializeCallCount);
        }

        [Fact]
        public async Task InitAsync_WhenFirstAttemptThrows_IsNotRetried()
        {
            var sut = CreateSut("MyApp");
            sut.InitializeException = new InvalidOperationException("bad config");

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.InitAsync());
            await sut.InitAsync();

            Assert.Equal(1, sut.InitializeCallCount);
        }
```

In `test/Test/Apps/TripTimer/TripTimerControllerTests.cs`, add `using Test.HostedServices;` after `using Test.Apps;`. Replace the class summary comment with:

```csharp
    /// <summary>
    /// TripTimerController over a real Conductor (ConductorTestHelper) whose registry is seeded
    /// through the internal RegisterApp seam.
    /// </summary>
```

Replace the method `CreateConductorWithApps` with:

```csharp
        private static Conductor CreateConductorWithApps(IEnumerable<IAwtrixApp> apps)
        {
            var conductor = ConductorTestHelper.Create();
            foreach (var app in apps)
            {
                conductor.RegisterApp(app);
            }
            return conductor;
        }
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.HostedServices.Conductor|FullyQualifiedName~Test.Apps.AwtrixAppTests|FullyQualifiedName~TripTimerControllerTests"`
Expected: build FAILS with `CS1061: 'Conductor' does not contain a definition for 'RegisterApp'` and `CS0117: 'AppNames' does not contain a definition for 'All'`.

- [ ] **Step 3: Implement the once-guard**

In `src/api/Apps/AwtrixApp.cs`:
1. Add the field `private int _initState;` directly below `private IAwtrixService AwtrixService;`.
2. Replace `InitAsync` with:

```csharp
        /// <summary>
        /// Runs at most once. A second call (or a call after a failed first attempt) logs and returns.
        /// </summary>
        public async Task InitAsync()
        {
            if (Interlocked.Exchange(ref _initState, 1) == 1)
            {
                Logger.LogWarning("InitAsync called more than once for {AppType} on {AwtrixAddress}; ignoring", Config.Type, AwtrixAddress);
                return;
            }

            await AppClear();

            Logger.LogInformation("Initializing {Config} for {AwtrixAddress}", Config.Type, AwtrixAddress);

            Initialize();
        }
```

- [ ] **Step 4: Implement Conductor (replace the whole file)**

`src/api/HostedServices/Conductor.cs`:

```csharp
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.Diurnal;
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Apps.SlackStatus;
using AwtrixSharpWeb.Apps.TripTimer;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AwtrixSharpWeb.HostedServices
{
    internal class AppNames
    {
        public const string DiurnalApp = "DiurnalApp";
        public const string ButtonApp = "ButtonApp";
        public const string TripTimerApp = "TripTimerApp";
        public const string SlackStatusApp = "SlackStatusApp";
        public const string MqttRenderApp = "MqttRenderApp";
        public const string MqttClockRenderApp = "MqttClockRenderApp";

        /// <summary>
        /// Every Type the factory can build, listed in "unknown app type" warnings.
        /// </summary>
        public static readonly string[] All = { DiurnalApp, ButtonApp, TripTimerApp, SlackStatusApp, MqttRenderApp, MqttClockRenderApp };
    }

    /// <summary>
    /// Orchestrates the various Awtrix apps based on configuration.
    /// Lifecycle: create every app, init each exactly once (isolated), bind buttons, register;
    /// on stop, dispose every registered app.
    /// </summary>
    public class Conductor : IHostedService
    {
        private readonly ILogger<Conductor> _logger;
        private readonly ISlackConnector _slackConnector;
        private readonly IMqttConnector _mqttConnector;
        private readonly IAwtrixService _awtrixService;
        private readonly ITripPlannerService _tripPlanner;
        private readonly ITimerService _timerService;
        private readonly IClock _clock;
        private readonly IHostEnvironment _hostEnvironment;
        private readonly ILoggerFactory _loggerFactory;
        private readonly AwtrixConfig _awtrixConfig;

        private readonly object _registryLock = new();
        private readonly List<RegisteredApp> _registry = new();
        private int _started;

        /// <summary>
        /// An app keyed by the device it drives and its configured Type.
        /// </summary>
        private sealed record RegisteredApp(AwtrixAddress? Device, string Type, IAwtrixApp App)
        {
            public string BaseTopic => Device?.BaseTopic ?? string.Empty;
        }

        public Conductor(
            ILogger<Conductor> logger
            , IHostEnvironment env
            , IOptions<AwtrixConfig> awtrixConfig
            , ITimerService timerService
            , ITripPlannerService tripPlanner
            , IAwtrixService awtrixService
            , ISlackConnector slackConnector
            , IMqttConnector mqttConnector
            , IClock clock
            , ILoggerFactory loggerFactory)
        {
            _logger = logger;
            _awtrixConfig = awtrixConfig.Value;
            _slackConnector = slackConnector;
            _awtrixService = awtrixService;
            _mqttConnector = mqttConnector;
            _tripPlanner = tripPlanner;
            _timerService = timerService;
            _clock = clock;
            _hostEnvironment = env;
            _loggerFactory = loggerFactory;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _started, 1) == 1)
            {
                _logger.LogWarning("Conductor.StartAsync called more than once; ignoring");
                return;
            }

            // Phase 1: create every app for every device. Nothing is initialised yet.
            var created = CreateApps();

            // Phase 2: initialise each app exactly once; a failure is isolated to that app.
            var initialised = await Task.WhenAll(created.Select(InitOneAsync));
            var running = created.Where((_, index) => initialised[index]).ToList();

            // Phase 3: device-local bindings between running apps, then register.
            foreach (var deviceApps in running.GroupBy(r => r.Device))
            {
                BindButtons(deviceApps.ToList());
            }

            lock (_registryLock)
            {
                _registry.AddRange(running);
            }

            _logger.LogInformation("Conductor started {Running} of {Created} app(s)", running.Count, created.Count);
        }

        private List<RegisteredApp> CreateApps()
        {
            var created = new List<RegisteredApp>();

            foreach (var device in _awtrixConfig.Devices ?? Array.Empty<DeviceConfig>())
            {
                if (device == null || string.IsNullOrWhiteSpace(device.BaseTopic))
                {
                    _logger.LogWarning("Skipping a device with no BaseTopic");
                    continue;
                }

                if (device.IsHttp)
                {
                    _logger.LogInformation(
                        "Device {Device} uses the HTTP transport; hardware buttons are not supported, ButtonApp not created",
                        device.BaseTopic);
                }
                else
                {
                    AddIfCreated(created, device, AppConfig.Empty().WithName(AppNames.ButtonApp));
                }

                foreach (var appConfig in device.Apps ?? new List<AppConfig>())
                {
                    AddIfCreated(created, device, appConfig);
                }
            }

            return created;
        }

        private void AddIfCreated(List<RegisteredApp> created, DeviceConfig device, AppConfig? appConfig)
        {
            if (appConfig == null || string.IsNullOrWhiteSpace(appConfig.Type))
            {
                _logger.LogWarning("Skipping an app with no Type on device {Device}", device.BaseTopic);
                return;
            }

            LogAppConfigDetails(appConfig);

            try
            {
                var app = AppFactory(device, appConfig);
                if (app != null)
                {
                    created.Add(new RegisteredApp(device, appConfig.Type, app));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create app {AppType} on device {Device}: {Reason}; skipping", appConfig.Type, device.BaseTopic, ex.Message);
            }
        }

        private async Task<bool> InitOneAsync(RegisteredApp entry)
        {
            try
            {
                await entry.App.InitAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialise app {AppType} on device {Device}: {Reason}; the app will not run", entry.Type, entry.BaseTopic, ex.Message);
                await DisposeOneAsync(entry, CancellationToken.None);
                return false;
            }
        }

        private void BindButtons(IReadOnlyList<RegisteredApp> deviceApps)
        {
            var buttonApp = deviceApps.Select(r => r.App).OfType<ButtonApp>().FirstOrDefault();
            if (buttonApp == null)
            {
                return;
            }

            var baseTopic = deviceApps[0].BaseTopic;

            buttonApp.Click += (s, e) =>
            {
                _logger.LogInformation("{Button} button clicked on {Device}", e.Button, baseTopic);
            };

            buttonApp.DoubleClick += (s, e) =>
            {
                _logger.LogInformation("{Button} button double-clicked on {Device}", e.Button, baseTopic);
            };

            // Right double-click starts this device's trip timer now
            foreach (var tripTimerApp in deviceApps.Select(r => r.App).OfType<TripTimerApp>())
            {
                buttonApp.DoubleClick += (s, e) =>
                {
                    if (e.Button != Button.Right)
                    {
                        return;
                    }

                    try
                    {
                        tripTimerApp.ExecuteNow();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Starting TripTimerApp from a double-click failed on {Device}", baseTopic);
                    }
                };
            }
        }

        private void LogAppConfigDetails(AppConfig appConfig)
        {
            var keysCount = appConfig.Config?.Count ?? 0;
            var valueMapsCount = appConfig.ValueMaps?.Count ?? 0;

            _logger.LogDebug(
                "App configuration: Type={Type}, Name={Name}, Keys.Count={KeysCount}, ValueMaps.Count={ValueMapsCount}",
                appConfig.Type,
                appConfig.Name,
                keysCount,
                valueMapsCount
            );
        }

        /// <summary>
        /// Builds an app for a known Type; returns null (logged) for an unknown Type.
        /// </summary>
        private IAwtrixApp? AppFactory(DeviceConfig device, AppConfig appConfig)
        {
            IAwtrixApp app;

            switch (appConfig.Type)
            {
                case AppNames.DiurnalApp:
                    {
                        var appLogger = _loggerFactory.CreateLogger<DiurnalApp>();
                        app = new DiurnalApp(appLogger, _clock, _timerService, appConfig, device, _awtrixService);
                    }
                    break;

                case AppNames.TripTimerApp:
                    {
                        var appLogger = _loggerFactory.CreateLogger<TripTimerApp>();
                        var tripTimerConfig = appConfig.As<TripTimerAppConfig>();
                        app = new TripTimerApp(appLogger, _clock, device, _awtrixService, _timerService, tripTimerConfig, _tripPlanner);
                    }
                    break;

                case AppNames.ButtonApp:
                    {
                        var appLogger = _loggerFactory.CreateLogger<ButtonApp>();
                        var mqttConfig = appConfig.As<MqttAppConfig>();
                        app = new ButtonApp(appLogger, mqttConfig, device, _awtrixService, _mqttConnector);
                    }
                    break;

                case AppNames.MqttRenderApp:
                    {
                        var appLogger = _loggerFactory.CreateLogger<MqttRenderApp>();
                        var mqttConfig = appConfig.As<MqttAppConfig>();
                        app = new MqttRenderApp(appLogger, _clock, mqttConfig, device, _awtrixService, _mqttConnector);
                    }
                    break;

                case AppNames.MqttClockRenderApp:
                    {
                        var appLogger = _loggerFactory.CreateLogger<MqttClockRenderApp>();
                        var mqttConfig = appConfig.As<MqttAppConfig>();
                        app = new MqttClockRenderApp(appLogger, _clock, mqttConfig, device, _awtrixService, _mqttConnector, _timerService);
                    }
                    break;

                case AppNames.SlackStatusApp:
                    {
                        var appLogger = _loggerFactory.CreateLogger<SlackStatusApp>();
                        var slackStatusConfig = appConfig.As<SlackStatusAppConfig>();
                        app = new SlackStatusApp(appLogger, slackStatusConfig, device, _awtrixService, _slackConnector);
                    }
                    break;

                default:
                    _logger.LogWarning(
                        "Unknown app type '{AppType}' on device '{Device}'; skipping. Known types: {KnownTypes}",
                        appConfig.Type,
                        device.BaseTopic,
                        string.Join(", ", AppNames.All));
                    return null;
            }

            _logger.LogInformation("Created {AppType} for {Device}", appConfig.Type, device.BaseTopic);

            return app;
        }

        public void ExecuteNow(string baseTopic, string appName)
        {
            // Interim: still builds a transient instance (CR-07). Task 3 replaces this with a registry lookup.
            try
            {
                var device = _awtrixConfig.Devices.FirstOrDefault(d => d.BaseTopic == baseTopic);
                if (device == null)
                {
                    _logger.LogWarning("Device with base topic '{BaseTopic}' not found", baseTopic);
                    return;
                }

                var config = device.Apps.FirstOrDefault(a => a.Type == appName);
                if (config == null)
                {
                    _logger.LogWarning("App '{AppName}' not found for device '{BaseTopic}'", appName, baseTopic);
                    return;
                }

                var app = AppFactory(device, config);
                if (app == null)
                {
                    return;
                }
                app.InitAsync().GetAwaiter().GetResult();
                app.ExecuteNow();
                _logger.LogInformation("Successfully executed app '{AppName}' on device '{BaseTopic}'", appName, baseTopic);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing app '{AppName}' on device '{BaseTopic}'", appName, baseTopic);
            }
        }

        /// <summary>
        /// Running apps of the given Type, optionally limited to one device (ordinal comparison).
        /// </summary>
        public List<IAwtrixApp> FindApps(string appType, string? baseTopic = null)
        {
            lock (_registryLock)
            {
                return _registry
                    .Where(r => Matches(r, appType, baseTopic))
                    .Select(r => r.App)
                    .ToList();
            }
        }

        /// <summary>
        /// Test seam: registers an already-constructed app, keyed by its address and config Type.
        /// </summary>
        internal void RegisterApp(IAwtrixApp app)
        {
            lock (_registryLock)
            {
                _registry.Add(new RegisteredApp(app.AwtrixAddress, app.GetConfig()?.Type ?? string.Empty, app));
            }
        }

        private static bool Matches(RegisteredApp entry, string appType, string? baseTopic)
        {
            return string.Equals(entry.Type, appType, StringComparison.Ordinal)
                && (baseTopic == null || string.Equals(entry.BaseTopic, baseTopic, StringComparison.Ordinal));
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Conductor stopping");

            List<RegisteredApp> apps;
            lock (_registryLock)
            {
                apps = _registry.ToList();
                _registry.Clear();
            }

            foreach (var entry in apps)
            {
                await DisposeOneAsync(entry, cancellationToken);
            }
        }

        private Task DisposeOneAsync(RegisteredApp entry, CancellationToken cancellationToken)
        {
            // Interim synchronous disposal; Task 4 awaits IAsyncDisposable with a timeout.
            try
            {
                entry.App.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing {AppType} on {Device}", entry.Type, entry.BaseTopic);
            }

            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.HostedServices.Conductor|FullyQualifiedName~Test.Apps.AwtrixAppTests|FullyQualifiedName~TripTimerControllerTests"`
Expected: PASS, 0 failed.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test`
Expected: 0 failed. `grep -n "NotImplementedException\|_apps" src/api/HostedServices/Conductor.cs` → no matches.

- [ ] **Step 7: Commit**

```bash
git add src/api/HostedServices/Conductor.cs src/api/Apps/AwtrixApp.cs test/Test/HostedServices/ConductorTestHelper.cs test/Test/HostedServices/ConductorTests.cs test/Test/HostedServices/ConductorStartupTests.cs test/Test/HostedServices/ConductorShutdownTests.cs test/Test/Apps/AwtrixAppTests.cs test/Test/Apps/TripTimer/TripTimerControllerTests.cs
git commit -m "fix(conductor): create all apps, init each once in isolation, skip unknown types (CR-05, CR-06, CR-30)

StartAsync creates every app, initialises each exactly once with per-app try/catch, then binds
buttons per device and registers running apps keyed by (BaseTopic, Type). Unknown or missing
Types are logged and skipped; init failures are logged, disposed and not registered.
AwtrixApp.InitAsync guards against a second call. ButtonApp gets its own logger category.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 3: `ExecuteNow` targets the running instance; controllers return 404/200/500 (CR-07)

**Files:**
- Create: `src/api/HostedServices/AppExecutionResult.cs`
- Create: `src/api/Controllers/AppExecutionResponses.cs`
- Modify: `src/api/HostedServices/Conductor.cs` (replace `ExecuteNow`)
- Modify: `src/api/Controllers/MqttRenderController.cs` (full replace)
- Modify: `src/api/Controllers/TripTimerController.cs` (full replace)
- Create: `test/Test/HostedServices/ConductorExecuteNowTests.cs`
- Modify: `test/Test/HostedServices/ConductorTests.cs` (delete two legacy tests)
- Modify: `test/Test/Controllers/MqttRenderControllerTests.cs` (full replace)
- Modify: `test/Test/Apps/TripTimer/TripTimerControllerTests.cs` (replace one test, add three)

**Interfaces:**
- Consumes (from Task 2): `Conductor.RegisterApp`, `Conductor.FindApps(string, string?)`, `Conductor.Matches`, the `RegisteredApp` record, `ConductorTestHelper.MockApp`.
- Produces:
  - `public enum AppExecutionResult { NotFound, Started, Error }`
  - `public AppExecutionResult Conductor.ExecuteNow(string baseTopic, string appType)`
  - `internal static IActionResult AppExecutionResponses.ToActionResult(this ControllerBase controller, AppExecutionResult result, string appName, string baseTopic)`
  - `TripTimerController.StartNow(...) : IActionResult`

- [ ] **Step 1: Write the failing tests**

Create `test/Test/HostedServices/ConductorExecuteNowTests.cs`:

```csharp
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;
using Moq;
using Test.Apps;

namespace Test.HostedServices
{
    /// <summary>
    /// CR-07: ExecuteNow drives the registered instance and never builds a transient, cron-armed duplicate.
    /// </summary>
    public class ConductorExecuteNowTests
    {
        [Fact]
        public void ExecuteNow_RunningApp_ExecutesThatInstanceAndReturnsStarted()
        {
            var conductor = ConductorTestHelper.Create();
            var app = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.TripTimerApp);
            conductor.RegisterApp(app.Object);

            var first = conductor.ExecuteNow("awtrix/clock1", AppNames.TripTimerApp);
            var second = conductor.ExecuteNow("awtrix/clock1", AppNames.TripTimerApp);

            Assert.Equal(AppExecutionResult.Started, first);
            Assert.Equal(AppExecutionResult.Started, second);
            app.Verify(a => a.ExecuteNow(), Times.Exactly(2));
            app.Verify(a => a.InitAsync(), Times.Never);
            Assert.Single(conductor.FindApps(AppNames.TripTimerApp));
        }

        [Fact]
        public async Task ExecuteNow_AfterStartAsync_DoesNotInitialiseAnotherInstance()
        {
            var device = new DeviceConfig { BaseTopic = "http://192.168.1.50/api" };
            device.Apps.Add(AppConfig.Empty().WithName(AppNames.DiurnalApp));
            var awtrix = new Mock<IAwtrixService>();
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                awtrixService: awtrix.Object,
                clock: new MockClock(new DateTimeOffset(2026, 9, 13, 0, 30, 0, TimeSpan.FromHours(10))));
            await conductor.StartAsync(CancellationToken.None);

            var result = conductor.ExecuteNow(device.BaseTopic, AppNames.DiurnalApp);

            Assert.Equal(AppExecutionResult.Started, result);
            awtrix.Verify(a => a.AppClear(device, AppNames.DiurnalApp), Times.Once);
        }

        [Fact]
        public void ExecuteNow_TargetsOnlyTheRequestedDevice()
        {
            var conductor = ConductorTestHelper.Create();
            var clock1 = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.TripTimerApp);
            var clock2 = ConductorTestHelper.MockApp("awtrix/clock2", AppNames.TripTimerApp);
            conductor.RegisterApp(clock1.Object);
            conductor.RegisterApp(clock2.Object);

            conductor.ExecuteNow("awtrix/clock2", AppNames.TripTimerApp);

            clock1.Verify(a => a.ExecuteNow(), Times.Never);
            clock2.Verify(a => a.ExecuteNow(), Times.Once);
        }

        [Theory]
        [InlineData("awtrix/does-not-exist", AppNames.TripTimerApp)]
        [InlineData("awtrix/clock1", "NoSuchApp")]
        [InlineData("", AppNames.TripTimerApp)]
        [InlineData("awtrix/clock1", "")]
        public void ExecuteNow_NoMatchingRunningApp_ReturnsNotFound(string baseTopic, string appType)
        {
            var conductor = ConductorTestHelper.Create();
            var app = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.TripTimerApp);
            conductor.RegisterApp(app.Object);

            var result = conductor.ExecuteNow(baseTopic, appType);

            Assert.Equal(AppExecutionResult.NotFound, result);
            app.Verify(a => a.ExecuteNow(), Times.Never);
        }

        [Fact]
        public void ExecuteNow_WhenAppThrows_ReturnsError()
        {
            var conductor = ConductorTestHelper.Create();
            var app = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.MqttRenderApp);
            app.Setup(a => a.ExecuteNow()).Throws(new ObjectDisposedException("cts"));
            conductor.RegisterApp(app.Object);

            var result = conductor.ExecuteNow("awtrix/clock1", AppNames.MqttRenderApp);

            Assert.Equal(AppExecutionResult.Error, result);
        }

        [Fact]
        public void ExecuteNow_DuplicateEntries_ExecutesBothAndReportsErrorIfEitherThrows()
        {
            var conductor = ConductorTestHelper.Create();
            var throwing = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.MqttRenderApp);
            throwing.Setup(a => a.ExecuteNow()).Throws(new InvalidOperationException("boom"));
            var healthy = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.MqttRenderApp);
            conductor.RegisterApp(throwing.Object);
            conductor.RegisterApp(healthy.Object);

            var result = conductor.ExecuteNow("awtrix/clock1", AppNames.MqttRenderApp);

            Assert.Equal(AppExecutionResult.Error, result);
            healthy.Verify(a => a.ExecuteNow(), Times.Once);
        }
    }
}
```

Replace the whole of `test/Test/Controllers/MqttRenderControllerTests.cs`:

```csharp
using AwtrixSharpWeb.Controllers;
using AwtrixSharpWeb.HostedServices;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Test.HostedServices;

namespace Test.Controllers
{
    /// <summary>
    /// MqttRenderController maps Conductor.ExecuteNow's result to 200/404/500. Uses a real Conductor
    /// (ConductorTestHelper) with mocked apps registered through RegisterApp.
    /// </summary>
    public class MqttRenderControllerTests
    {
        [Fact]
        public void StartNow_RunningApp_ReturnsOkWithMessageMentioningAppAndDevice()
        {
            var conductor = ConductorTestHelper.Create();
            var app = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.MqttRenderApp);
            conductor.RegisterApp(app.Object);
            var controller = new MqttRenderController(conductor);

            var result = controller.StartNow("awtrix/clock1", AppNames.MqttRenderApp);

            var okResult = Assert.IsType<OkObjectResult>(result);
            var message = okResult.Value!.ToString();
            Assert.Contains("MqttRenderApp", message);
            Assert.Contains("awtrix/clock1", message);
            app.Verify(a => a.ExecuteNow(), Times.Once);
        }

        [Fact]
        public void StartNow_UsesDefaultParameters_WhenNoneSupplied()
        {
            var conductor = ConductorTestHelper.Create();
            conductor.RegisterApp(ConductorTestHelper.MockApp("awtrix/clock1", AppNames.MqttRenderApp).Object);
            var controller = new MqttRenderController(conductor);

            var result = controller.StartNow();

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public void StartNow_AppNotRunningOnDevice_Returns404()
        {
            var conductor = ConductorTestHelper.Create();
            var controller = new MqttRenderController(conductor);

            var result = controller.StartNow("awtrix/clock1", AppNames.MqttRenderApp);

            var notFound = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Contains("awtrix/clock1", notFound.Value!.ToString());
        }

        [Fact]
        public void StartNow_AppThrows_Returns500()
        {
            var conductor = ConductorTestHelper.Create();
            var app = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.MqttRenderApp);
            app.Setup(a => a.ExecuteNow()).Throws(new InvalidOperationException("boom"));
            conductor.RegisterApp(app.Object);
            var controller = new MqttRenderController(conductor);

            var result = controller.StartNow("awtrix/clock1", AppNames.MqttRenderApp);

            Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
        }
    }
}
```

In `test/Test/Apps/TripTimer/TripTimerControllerTests.cs`:
1. Add `using AwtrixSharpWeb.Interfaces;` if it is not present (it is present today) and `using Moq;` (present today).
2. Replace the test `TestTimingConfig_NoTripTimerAppRegistered_Throws` (the whole method, including its `[Fact]`) with:

```csharp
        [Fact]
        public void TestTimingConfig_NoTripTimerAppRegistered_Returns404()
        {
            var conductor = CreateConductorWithApps(Array.Empty<IAwtrixApp>());
            var sut = new TripTimerController(conductor);

            var result = sut.TestTimingConfig("2025-09-01 06:41");

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public void StartNow_RunningApp_Returns200AndExecutesIt()
        {
            var app = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.TripTimerApp);
            var conductor = CreateConductorWithApps(new[] { app.Object });
            var sut = new TripTimerController(conductor);

            var result = sut.StartNow("awtrix/clock1", AppNames.TripTimerApp);

            Assert.IsType<OkObjectResult>(result);
            app.Verify(a => a.ExecuteNow(), Times.Once);
        }

        [Fact]
        public void StartNow_UnknownDevice_Returns404()
        {
            var app = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.TripTimerApp);
            var conductor = CreateConductorWithApps(new[] { app.Object });
            var sut = new TripTimerController(conductor);

            var result = sut.StartNow("awtrix/clock9", AppNames.TripTimerApp);

            Assert.IsType<NotFoundObjectResult>(result);
            app.Verify(a => a.ExecuteNow(), Times.Never);
        }

        [Fact]
        public void StartNow_AppThrows_Returns500()
        {
            var app = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.TripTimerApp);
            app.Setup(a => a.ExecuteNow()).Throws(new InvalidOperationException("boom"));
            var conductor = CreateConductorWithApps(new[] { app.Object });
            var sut = new TripTimerController(conductor);

            var result = sut.StartNow("awtrix/clock1", AppNames.TripTimerApp);

            Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
        }
```

In `test/Test/HostedServices/ConductorTests.cs`, delete the two methods `ExecuteNow_UnknownDevice_DoesNotThrow` and `ExecuteNow_UnknownAppOnKnownDevice_DoesNotThrow`, each with its `[Fact]` attribute. `ConductorExecuteNowTests` supersedes them.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~ConductorExecuteNowTests|FullyQualifiedName~MqttRenderControllerTests|FullyQualifiedName~TripTimerControllerTests"`
Expected: build FAILS with `CS0103: The name 'AppExecutionResult' does not exist` and `CS0815: Cannot assign void to an implicitly-typed variable` (TripTimerController.StartNow).

- [ ] **Step 3: Implement**

Create `src/api/HostedServices/AppExecutionResult.cs`:

```csharp
namespace AwtrixSharpWeb.HostedServices
{
    /// <summary>
    /// Outcome of <see cref="Conductor.ExecuteNow"/>.
    /// </summary>
    public enum AppExecutionResult
    {
        /// <summary>No running app of that Type on that device.</summary>
        NotFound,

        /// <summary>ExecuteNow was invoked on every matching app without throwing.</summary>
        Started,

        /// <summary>At least one matching app threw from ExecuteNow.</summary>
        Error
    }
}
```

In `src/api/HostedServices/Conductor.cs`, replace the whole `public void ExecuteNow(string baseTopic, string appName)` method (including its interim comment) with:

```csharp
        /// <summary>
        /// Runs the registered instance(s) of <paramref name="appType"/> on the device <paramref name="baseTopic"/> now.
        /// Never creates, initialises or disposes an app, and never throws.
        /// </summary>
        public AppExecutionResult ExecuteNow(string baseTopic, string appType)
        {
            if (string.IsNullOrWhiteSpace(baseTopic) || string.IsNullOrWhiteSpace(appType))
            {
                _logger.LogWarning("ExecuteNow requires a base topic and an app type (got '{BaseTopic}', '{AppType}')", baseTopic, appType);
                return AppExecutionResult.NotFound;
            }

            List<RegisteredApp> matches;
            lock (_registryLock)
            {
                matches = _registry.Where(r => Matches(r, appType, baseTopic)).ToList();
            }

            if (matches.Count == 0)
            {
                _logger.LogWarning("ExecuteNow: no running app '{AppType}' on device '{BaseTopic}'", appType, baseTopic);
                return AppExecutionResult.NotFound;
            }

            var result = AppExecutionResult.Started;
            foreach (var entry in matches)
            {
                try
                {
                    entry.App.ExecuteNow();
                    _logger.LogInformation("Executed app '{AppType}' on device '{BaseTopic}'", entry.Type, entry.BaseTopic);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing app '{AppType}' on device '{BaseTopic}'", entry.Type, entry.BaseTopic);
                    result = AppExecutionResult.Error;
                }
            }

            return result;
        }
```

Create `src/api/Controllers/AppExecutionResponses.cs`:

```csharp
using AwtrixSharpWeb.HostedServices;
using Microsoft.AspNetCore.Mvc;

namespace AwtrixSharpWeb.Controllers
{
    /// <summary>
    /// Maps Conductor.ExecuteNow results to HTTP responses: Started 200, NotFound 404, Error 500.
    /// </summary>
    internal static class AppExecutionResponses
    {
        public static IActionResult ToActionResult(this ControllerBase controller, AppExecutionResult result, string appName, string baseTopic)
        {
            return result switch
            {
                AppExecutionResult.Started => controller.Ok(new { message = $"App '{appName}' started on device '{baseTopic}'" }),
                AppExecutionResult.NotFound => controller.NotFound(new { message = $"App '{appName}' is not running on device '{baseTopic}'" }),
                _ => controller.StatusCode(StatusCodes.Status500InternalServerError,
                        new { message = $"App '{appName}' failed to start on device '{baseTopic}'; see the service log" })
            };
        }
    }
}
```

Replace the whole of `src/api/Controllers/MqttRenderController.cs`:

```csharp
using AwtrixSharpWeb.HostedServices;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace AwtrixSharpWeb.Controllers
{

    [SwaggerTag("Apps")]
    [Route("api/app/[controller]")]
    [ApiController]
    public class MqttRenderController : ControllerBase
    {
        private readonly Conductor _conductor;

        public MqttRenderController(Conductor conductor)
        {
            _conductor = conductor;
        }

        /// <summary>
        /// Triggers an app to execute immediately
        /// </summary>
        /// <param name="deviceAddress">The Awtrix device address (topic)</param>
        /// <param name="appName">The name of the app to execute</param>
        [HttpPost("start")]
        [SwaggerOperation(
            Summary = "Start an app immediately",
            Description = "Triggers the specified app to execute immediately on the specified Awtrix device",
            OperationId = "StartApp"
        )]
        [SwaggerResponse(200, "App started successfully")]
        [SwaggerResponse(404, "The app is not running on that device")]
        [SwaggerResponse(500, "The app failed to start")]
        public IActionResult StartNow(
            [FromQuery, SwaggerParameter("The Awtrix device address/topic")]
            string deviceAddress = "awtrix/clock1",

            [FromQuery, SwaggerParameter("The name of the app to execute")]
            string appName = AppNames.MqttRenderApp)
        {
            var result = _conductor.ExecuteNow(deviceAddress, appName);
            return this.ToActionResult(result, appName, deviceAddress);
        }
    }
}
```

Replace the whole of `src/api/Controllers/TripTimerController.cs`:

```csharp
using AwtrixSharpWeb.Apps.TripTimer;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;


namespace AwtrixSharpWeb.Controllers
{
    [SwaggerTag("Apps")]
    [Route("api/app/[controller]")]
    [ApiController]
    public class TripTimerController : ControllerBase
    {
        private readonly Conductor _conductor;

        public TripTimerController(Conductor conductor)
        {
            _conductor = conductor;
        }

        /// <summary>
        /// Start now
        /// </summary>
        [HttpPost("start")]
        [SwaggerResponse(200, "App started successfully")]
        [SwaggerResponse(404, "The app is not running on that device")]
        [SwaggerResponse(500, "The app failed to start")]
        public IActionResult StartNow([FromQuery] string baseTopic = "awtrix/clock1", [FromQuery] string appName = AppNames.TripTimerApp)
        {
            var result = _conductor.ExecuteNow(baseTopic, appName);
            return this.ToActionResult(result, appName, baseTopic);
        }


        [HttpPost("test/alarm-timings")]
        public IActionResult TestTimingConfig([FromQuery] string departureTime = "2025-09-01 06:41")
        {
            var dateTime = DateTimeOffset.Parse(departureTime);

            var tripTimer = _conductor
                            .FindApps(AppNames.TripTimerApp)
                            .OfType<TripTimerApp>()
                            .FirstOrDefault();

            if (tripTimer == null)
            {
                return NotFound(new { message = "No TripTimerApp is running" });
            }

            var alarmSegments = tripTimer.GetAlarmTime(TripSummary.Factory(dateTime));

            return Ok(alarmSegments);
        }
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~ConductorExecuteNowTests|FullyQualifiedName~MqttRenderControllerTests|FullyQualifiedName~TripTimerControllerTests|FullyQualifiedName~Test.HostedServices.ConductorTests"`
Expected: PASS, 0 failed.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: 0 failed. `grep -n "AppFactory(device, config)\|InitAsync().GetAwaiter" src/api/HostedServices/Conductor.cs` → no matches.

- [ ] **Step 6: Commit**

```bash
git add src/api/HostedServices/AppExecutionResult.cs src/api/Controllers/AppExecutionResponses.cs src/api/HostedServices/Conductor.cs src/api/Controllers/MqttRenderController.cs src/api/Controllers/TripTimerController.cs test/Test/HostedServices/ConductorExecuteNowTests.cs test/Test/HostedServices/ConductorTests.cs test/Test/Controllers/MqttRenderControllerTests.cs test/Test/Apps/TripTimer/TripTimerControllerTests.cs
git commit -m "fix(conductor): ExecuteNow targets the running instance; start endpoints return 404/200/500 (CR-07)

ExecuteNow looks up registered apps by (BaseTopic, Type) and returns NotFound/Started/Error instead of
building, initialising and leaking a cron-armed duplicate. TripTimer and MqttRender start endpoints map
the result to 404/200/500; TestTimingConfig returns 404 when no TripTimerApp is running.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 4: Bounded async app disposal and a null-safe SlackConnector stop (CR-16)

**Files:**
- Modify: `src/api/Interfaces/IAwtrixApp.cs` (full replace)
- Modify: `src/api/Apps/AwtrixApp.cs` (add `DisposeAsync`)
- Modify: `src/api/Apps/ScheduledApp.cs` (add `DisposeAsync` override)
- Modify: `src/api/HostedServices/Conductor.cs` (replace `StopAsync` and `DisposeOneAsync`, add constant)
- Modify: `src/api/HostedServices/SlackConnector.cs` (replace `StopAsync`)
- Modify: `test/Test/HostedServices/ConductorShutdownTests.cs` (full replace)
- Create: `test/Test/HostedServices/SlackConnectorTests.cs`
- Modify: `test/Test/Apps/AwtrixAppTests.cs` (one test)
- Modify: `test/Test/Apps/ScheduledAppTests.cs` (`TestScheduledApp` and one test)

**Interfaces:**
- Consumes (from Task 2): `Conductor.DisposeOneAsync(RegisteredApp, CancellationToken)`, `RegisteredApp`, `ConductorTestHelper.MockApp`. From Task 3: `Conductor.ExecuteNow : AppExecutionResult`.
- Produces:
  - `IAwtrixApp : IDisposable, IAsyncDisposable`
  - `public virtual ValueTask AwtrixApp<TConfig>.DisposeAsync()`
  - `public override ValueTask ScheduledApp<TConfig>.DisposeAsync()`
  - `internal static readonly TimeSpan Conductor.AppDisposeTimeout` (10 s)

- [ ] **Step 1: Write the failing tests**

Replace the whole of `test/Test/HostedServices/ConductorShutdownTests.cs`:

```csharp
using AwtrixSharpWeb.HostedServices;
using Moq;

namespace Test.HostedServices
{
    /// <summary>
    /// Conductor.StopAsync awaits each app's DisposeAsync in isolation, bounded by a per-app timeout
    /// and the host shutdown token, and empties the registry first.
    /// </summary>
    public class ConductorShutdownTests
    {
        [Fact]
        public async Task StopAsync_WithNoRegisteredApps_CompletesWithoutError()
        {
            var conductor = ConductorTestHelper.Create();

            var exception = await Record.ExceptionAsync(() => conductor.StopAsync(CancellationToken.None));

            Assert.Null(exception);
        }

        [Fact]
        public async Task StopAsync_DisposesEveryRegisteredAppAsynchronously()
        {
            var conductor = ConductorTestHelper.Create();
            var app1 = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.DiurnalApp);
            var app2 = ConductorTestHelper.MockApp("awtrix/clock2", AppNames.DiurnalApp);
            conductor.RegisterApp(app1.Object);
            conductor.RegisterApp(app2.Object);

            await conductor.StopAsync(CancellationToken.None);

            app1.Verify(a => a.DisposeAsync(), Times.Once);
            app2.Verify(a => a.DisposeAsync(), Times.Once);
            app1.Verify(a => a.Dispose(), Times.Never);
            app2.Verify(a => a.Dispose(), Times.Never);
        }

        [Fact]
        public async Task StopAsync_AwaitsPendingAsyncDisposal()
        {
            var conductor = ConductorTestHelper.Create();
            var gate = new TaskCompletionSource();
            var app = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.MqttRenderApp);
            app.Setup(a => a.DisposeAsync()).Returns(new ValueTask(gate.Task));
            conductor.RegisterApp(app.Object);

            var stop = conductor.StopAsync(CancellationToken.None);

            Assert.False(stop.IsCompleted);
            gate.SetResult();
            await stop.WaitAsync(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task StopAsync_WhenOneAppDisposeThrows_StillDisposesOthers()
        {
            var conductor = ConductorTestHelper.Create();
            var throwing = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.DiurnalApp);
            throwing.Setup(a => a.DisposeAsync()).Throws(new InvalidOperationException("offline"));
            var faulting = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.MqttRenderApp);
            faulting.Setup(a => a.DisposeAsync()).Returns(new ValueTask(Task.FromException(new HttpRequestException("offline"))));
            var healthy = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.SlackStatusApp);
            conductor.RegisterApp(throwing.Object);
            conductor.RegisterApp(faulting.Object);
            conductor.RegisterApp(healthy.Object);

            var exception = await Record.ExceptionAsync(() => conductor.StopAsync(CancellationToken.None));

            Assert.Null(exception);
            healthy.Verify(a => a.DisposeAsync(), Times.Once);
        }

        [Fact]
        public async Task StopAsync_WhenShutdownTokenIsCancelled_DoesNotWaitForAHungApp()
        {
            var conductor = ConductorTestHelper.Create();
            var hung = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.TripTimerApp);
            hung.Setup(a => a.DisposeAsync()).Returns(new ValueTask(new TaskCompletionSource().Task));
            var healthy = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.DiurnalApp);
            conductor.RegisterApp(hung.Object);
            conductor.RegisterApp(healthy.Object);
            using var shutdown = new CancellationTokenSource();
            shutdown.Cancel();

            await conductor.StopAsync(shutdown.Token).WaitAsync(TimeSpan.FromSeconds(5));

            hung.Verify(a => a.DisposeAsync(), Times.Once);
            healthy.Verify(a => a.DisposeAsync(), Times.Once);
        }

        [Fact]
        public async Task StopAsync_ClearsRegistry_SoExecuteNowReturnsNotFound()
        {
            var conductor = ConductorTestHelper.Create();
            var app = ConductorTestHelper.MockApp("awtrix/clock1", AppNames.TripTimerApp);
            conductor.RegisterApp(app.Object);

            await conductor.StopAsync(CancellationToken.None);

            Assert.Empty(conductor.FindApps(AppNames.TripTimerApp));
            Assert.Equal(AppExecutionResult.NotFound, conductor.ExecuteNow("awtrix/clock1", AppNames.TripTimerApp));
            app.Verify(a => a.ExecuteNow(), Times.Never);
        }

        [Fact]
        public void AppDisposeTimeout_CoversTwoHttpPublishTimeouts()
        {
            Assert.Equal(TimeSpan.FromSeconds(10), Conductor.AppDisposeTimeout);
        }
    }
}
```

Create `test/Test/HostedServices/SlackConnectorTests.cs`:

```csharp
using System.Reflection;
using AwtrixSharpWeb.HostedServices;
using Microsoft.Extensions.Logging.Abstractions;

namespace Test.HostedServices
{
    /// <summary>
    /// CR-16: stopping the host without Slack configured must not throw.
    /// </summary>
    public class SlackConnectorTests
    {
        private const BindingFlags AnyField = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        [Fact]
        public async Task StopAsync_WhenSlackWasNeverConnected_DoesNotThrow()
        {
            var connector = new SlackConnector(NullLogger<SlackConnector>.Instance);
            // State after StartAsync ran without AWTRIXSHARP_SLACK__APPTOKEN: the executing task
            // finished early and no socket client was created. Set directly so the test ignores the
            // developer's environment.
            typeof(SlackConnector).GetField("_executingTask", AnyField)!.SetValue(connector, Task.CompletedTask);
            typeof(SlackConnector).GetField("_slackSocketClient", AnyField)!.SetValue(connector, null);

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
    }
}
```

In `test/Test/Apps/AwtrixAppTests.cs`, add inside `public class AwtrixAppTests`, after `Dispose_CallsAppClear`:

```csharp
        [Fact]
        public async Task DisposeAsync_AwaitsAppClearWithoutBlocking()
        {
            var sut = CreateSut("MyApp");
            var gate = new TaskCompletionSource<bool>();
            _mockAwtrixService.Setup(x => x.AppClear(_address, "MyApp")).Returns(gate.Task);

            var dispose = sut.DisposeAsync();

            Assert.False(dispose.IsCompleted);
            gate.SetResult(true);
            await dispose;
            _mockAwtrixService.Verify(x => x.AppClear(_address, "MyApp"), Times.Once);
        }
```

In `test/Test/Apps/ScheduledAppTests.cs`, replace in `TestScheduledApp`:

```csharp
        protected override async Task ActivateScheduledWork(CancellationTokenSource cts)
        {
            ActivateCallCount++;
            if (WaitForCancellationOnActivate)
            {
                await WaitForCancellation(cts.Token);
            }
        }
```

with:

```csharp
        /// <summary>
        /// Completes when an activation that waited for cancellation has ended.
        /// </summary>
        public TaskCompletionSource ActivationEnded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task ActivateScheduledWork(CancellationTokenSource cts)
        {
            ActivateCallCount++;
            if (WaitForCancellationOnActivate)
            {
                await WaitForCancellation(cts.Token);
                ActivationEnded.TrySetResult();
            }
        }
```

Then add inside `public class ScheduledAppTests`, after `Dispose_CancelsPendingWorkAndClearsApp`:

```csharp
        [Fact]
        public async Task DisposeAsync_EndsActiveRunAndAwaitsDismissAndClear()
        {
            var sut = CreateSut(type: "MyApp");
            sut.WaitForCancellationOnActivate = true;
            sut.ExecuteNow();

            await sut.DisposeAsync();

            await sut.ActivationEnded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            _mockAwtrixService.Verify(x => x.Dismiss(_address), Times.Once);
            _mockAwtrixService.Verify(x => x.AppClear(_address, "MyApp"), Times.AtLeast(2)); // WakeUp's clear + dispose's clear
        }
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~ConductorShutdownTests|FullyQualifiedName~SlackConnectorTests|FullyQualifiedName~Test.Apps.AwtrixAppTests|FullyQualifiedName~Test.Apps.ScheduledAppTests"`
Expected: build FAILS with `CS1061: 'IAwtrixApp' does not contain a definition for 'DisposeAsync'` and `CS0117: 'Conductor' does not contain a definition for 'AppDisposeTimeout'`.

- [ ] **Step 3: Implement the disposal seam**

Replace the whole of `src/api/Interfaces/IAwtrixApp.cs`:

```csharp
using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Interfaces
{
    /// <summary>
    /// An app driving one Awtrix device. Lifecycle, owned by Conductor:
    /// <list type="number">
    /// <item>Construct: no publishing, subscribing or timers.</item>
    /// <item><see cref="InitAsync"/>: at most once, after every app for every device is constructed; may run concurrently with other apps.</item>
    /// <item><see cref="ExecuteNow"/>: zero or more times, from controller threads or the MQTT receive thread.</item>
    /// <item><see cref="IAsyncDisposable.DisposeAsync"/>: exactly once (also after a failed init), before the MQTT connector
    /// stops, abandoned after Conductor.AppDisposeTimeout. Conductor never calls the synchronous Dispose.</item>
    /// </list>
    /// </summary>
    public interface IAwtrixApp : IDisposable, IAsyncDisposable
    {
        public AwtrixAddress AwtrixAddress { get; }

        public IAppConfig GetConfig();

        /// <summary>
        /// Clears the app's slot and wires its subscriptions/schedule. Must not block on the network
        /// beyond the publisher timeouts.
        /// </summary>
        Task InitAsync();

        void ExecuteNow();
    }
}
```

In `src/api/Apps/AwtrixApp.cs`, add directly below the existing `public void Dispose()` method:

```csharp
        /// <summary>
        /// Shutdown path used by Conductor: clears this app's custom slot without blocking.
        /// WS4 replaces this with one virtual dispose pattern across AwtrixApp/ScheduledApp/TripTimerApp.
        /// </summary>
        public virtual async ValueTask DisposeAsync()
        {
            await AppClear();
        }
```

In `src/api/Apps/ScheduledApp.cs`, add directly below `public void Dispose() { Dispose(true); }`:

```csharp
        /// <summary>
        /// Cancels any active run (its finally block deactivates) and awaits the final clears so they
        /// are published before the MQTT connector stops.
        /// </summary>
        public override async ValueTask DisposeAsync()
        {
            Logger.LogInformation("Disposing app {App}", Config.Name);

            Dispose(_cts);

            await Dismiss();
            await AppClear();
        }
```

- [ ] **Step 4: Implement bounded async stop in Conductor**

In `src/api/HostedServices/Conductor.cs`:
1. Add directly below `private int _started;`:

```csharp
        /// <summary>
        /// Per-app disposal budget: two sequential 5 s HTTP publishes (Dismiss + AppClear) to an offline device.
        /// </summary>
        internal static readonly TimeSpan AppDisposeTimeout = TimeSpan.FromSeconds(10);
```

2. Replace the whole `StopAsync` method and the whole `DisposeOneAsync` method with:

```csharp
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Conductor stopping");

            List<RegisteredApp> apps;
            lock (_registryLock)
            {
                apps = _registry.ToList();
                _registry.Clear();
            }

            await Task.WhenAll(apps.Select(entry => DisposeOneAsync(entry, cancellationToken)));

            _logger.LogInformation("Conductor stopped; disposed {Count} app(s)", apps.Count);
        }

        /// <summary>
        /// Awaits the app's DisposeAsync, bounded by <see cref="AppDisposeTimeout"/> and the shutdown token. Never throws.
        /// </summary>
        private async Task DisposeOneAsync(RegisteredApp entry, CancellationToken cancellationToken)
        {
            try
            {
                await entry.App.DisposeAsync().AsTask().WaitAsync(AppDisposeTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Disposing {AppType} on {Device} did not finish within {Timeout}; continuing shutdown", entry.Type, entry.BaseTopic, AppDisposeTimeout);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Shutdown timeout reached while disposing {AppType} on {Device}", entry.Type, entry.BaseTopic);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing {AppType} on {Device}", entry.Type, entry.BaseTopic);
            }
        }
```

- [ ] **Step 5: Implement the SlackConnector fix**

In `src/api/HostedServices/SlackConnector.cs`, replace the whole `StopAsync` method with:

```csharp
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Slack connector service");

            if (_executingTask == null)
            {
                return;
            }

            try
            {
                // Signal cancellation to the executing method
                _stoppingCts?.Cancel();

                // Null when Slack is not configured (no app token): nothing to disconnect
                _slackSocketClient?.Disconnect();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while disconnecting from Slack");
            }
            finally
            {
                // Wait until the task completes or the stop token triggers
                // Use a timeout to avoid hanging indefinitely
                var completedTask = await Task.WhenAny(_executingTask, Task.Delay(5000, cancellationToken));

                if (completedTask != _executingTask)
                {
                    _logger.LogWarning("Slack connector service shutdown timed out");
                }
            }

            _logger.LogInformation("Slack connector service stopped");
        }
```

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~ConductorShutdownTests|FullyQualifiedName~SlackConnectorTests|FullyQualifiedName~Test.Apps.AwtrixAppTests|FullyQualifiedName~Test.Apps.ScheduledAppTests|FullyQualifiedName~ConductorStartupTests"`
Expected: PASS, 0 failed. `ConductorStartupTests.StartAsync_ScheduledAppWithoutCronSchedule_IsSkippedAndDisposed` now exercises `ScheduledApp.DisposeAsync`.

- [ ] **Step 7: Run the full suite and code checks**

Run: `dotnet test`
Expected: 0 failed.

Run each check. The expected result follows each arrow.
- `grep -n "\.Result\|\.Wait()" src/api/HostedServices/Conductor.cs src/api/Apps/Buttons/ButtonApp.cs` → no matches.
- `grep -n "Dispose()" src/api/HostedServices/Conductor.cs` → no matches. Conductor only calls `DisposeAsync`.
- `grep -n "_slackSocketClient.Disconnect" src/api/HostedServices/SlackConnector.cs` → no matches.

- [ ] **Step 8: Commit**

```bash
git add src/api/Interfaces/IAwtrixApp.cs src/api/Apps/AwtrixApp.cs src/api/Apps/ScheduledApp.cs src/api/HostedServices/Conductor.cs src/api/HostedServices/SlackConnector.cs test/Test/HostedServices/ConductorShutdownTests.cs test/Test/HostedServices/SlackConnectorTests.cs test/Test/Apps/AwtrixAppTests.cs test/Test/Apps/ScheduledAppTests.cs
git commit -m "fix(lifecycle): bounded async app disposal at shutdown; SlackConnector stops without a client (CR-16)

IAwtrixApp gains IAsyncDisposable. Conductor.StopAsync empties the registry, then awaits every app's
DisposeAsync concurrently, each in try/catch and bounded by a 10 s per-app timeout and the host token.
ScheduledApp awaits its final Dismiss/AppClear. SlackConnector.StopAsync no longer throws when Slack
was never configured.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

## Self-review (completed by planner)

| Spec item | Task |
|---|---|
| D1 `InitAsync` rename and all call sites | T1 |
| D2 ButtonApp non-blocking subscribe | T1 |
| D3 three-phase start, concurrent init, per-device binding, second-start guard | T2 |
| D4 init exactly once (guard, no retry) | T2 |
| D5 per-app create/init isolation, unknown/missing Type, init-failure dispose, ButtonApp logger | T2 (dispose made async in T4) |
| D6 registry, `FindApps(type, baseTopic)`, `RegisterApp`, `ExecuteNow` result, controllers, `TestTimingConfig` 404 | T2 (registry), T3 |
| D7 registry cleared before disposal | T2 (sync), T4 |
| D8 `IAsyncDisposable` seam, bounded concurrent disposal | T4 |
| D9 SlackConnector null client | T4 |
| Acceptance CR-05 §1-5 | T1 (§1-3), T2 (§4-5), T4 Step 7 greps |
| Acceptance CR-06 §1-4 | T2 |
| Acceptance CR-07 §1-5 | T3 |
| Acceptance CR-16 §1-2 | T4 |
| Acceptance CR-30 §1-3 | T2 |
| Acceptance Shutdown §1-6 | T4 |

- **Placeholder scan:** none. Every code step has complete code. The Task 1 rename is an exact `sed` command with a grep check.
- **Type consistency:**
  - `RegisteredApp(AwtrixAddress? Device, string Type, IAwtrixApp App)`, `Matches`, `DisposeOneAsync(RegisteredApp, CancellationToken)`, `RegisterApp(IAwtrixApp)` and `FindApps(string, string?)` are defined in T2 and used unchanged in T3/T4.
  - `AppExecutionResult` is defined in T3 and used in T4 tests.
  - `ConductorTestHelper.MockApp` and the extra `Create` parameters (`timerService`, `tripPlanner`, `slackConnector`) are defined in T2.
  - `TestAwtrixApp.InitializeException` is defined in T2. `TestScheduledApp.ActivationEnded` is defined in T4.
- **Intermediate states compile:**
  - T1 keeps the old Conductor structure with `await app.InitAsync()`.
  - T2's interim `ExecuteNow` is `void`, so T2's legacy `ExecuteNow_*` tests compile; T3 deletes them.
  - T2's interim `ConductorShutdownTests` verify the sync `Dispose`; T4 replaces the file.
- **Test determinism:**
  - Blocking scenarios run on `Task.Run` with `WaitAsync(5 s)`, so a regression fails instead of hanging.
  - The button → TripTimer → `Notify` chain completes synchronously over Moq's completed tasks.
  - The `ScheduledApp` continuation is awaited through a `RunContinuationsAsynchronously` TCS, because xUnit's synchronization context may post it.
