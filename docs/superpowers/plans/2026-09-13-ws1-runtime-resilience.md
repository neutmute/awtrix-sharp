# WS1 Runtime Resilience Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Clock ticks can never crash the process, and publishers never throw. HTTP custom apps get correct URLs, and the core services become unit-testable through interfaces and `TimeProvider`.

**Architecture:**
- `TimerService` becomes a single sequential `PeriodicTimer` loop over an injected `TimeProvider`. It calls each subscriber in isolation.
- App tick handlers hand their async work to `AwtrixApp.FireAndLog`.
- Publishers (`HttpPublisher` over `IHttpClientFactory`, `MqttPublisher` over `IMqttConnector`) catch everything and return honest `bool`s. `AwtrixService` adds a defensive `SafePublish` wrapper and lets the publisher build custom-app URLs.
- `Conductor` depends only on interfaces, and DI registration moves into a testable `Program.AddAwtrixServices`.

**Tech Stack:** .NET 10, ASP.NET Core, xUnit 2.9, Moq 4.20, MQTTnet 5, `Microsoft.Extensions.TimeProvider.Testing` 10.0.0 (new, test project only)

**Spec:** `docs/superpowers/specs/2026-09-13-ws1-runtime-resilience-design.md`

## Global Constraints

- Config backward compatible: no `appsettings.json` keys or `AWTRIXSHARP_*` / `TRANSPORTOPENDATA__APIKEY` env vars added, renamed or reinterpreted. `BaseTopic` meaning is unchanged.
- No new required configuration. HTTP timeout `TimeSpan.FromSeconds(5)` and tick interval `TimeSpan.FromMilliseconds(100)` are code constants.
- MQTT topic strings stay byte-for-byte identical. HTTP URLs are unchanged except custom apps, which become `{base-without-trailing-slash}/custom?name={Uri.EscapeDataString(app)}`.
- Target framework `net10.0`. The only new package is `Microsoft.Extensions.TimeProvider.Testing` version `10.0.0`, in `test/Test/Test.csproj`.
- `ClockTickEventArgs.Time` = local wall-clock time, truncated to the whole second, `DateTimeKind.Local`.
- Publisher contract: `Publish(...)` never throws; `true` only on confirmed hand-off.
- Do not implement WS2-WS5 items (MqttConnector rebuild, InitAsync, ScheduledApp state machine, Diurnal catch-up, Slack handler body).
- Every task runs TDD: failing test, then implementation, then the filtered test passes, then the full `dotnet test` passes with 0 failures.
- Commits use explicit paths (`git add <paths>`), never `git add -A` or `git add .`. Every commit message ends with exactly:
  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt
  ```
- Run all commands from the repo root `C:\CodeMine\awtrix-sharp`.

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/api/HostedServices/TimerService.cs` | TimeProvider-driven, non-overlapping tick loop; isolated subscriber invocation | 1 |
| `test/Test/Test.csproj` | add FakeTimeProvider package | 1 |
| `test/Test/HostedServices/TimerServiceEventTests.cs` | rewritten for FakeTimeProvider | 1 |
| `src/api/Services/AwtrixPublisher.cs` | publisher contract doc, `protected Logger`, `BuildCustomAppUrl` (T3) | 2, 3 |
| `src/api/Services/HttpPublisher.cs` | IHttpClientFactory, 5 s named client, never throws; HTTP custom URL (T3) | 2, 3 |
| `src/api/Services/MqttPublisher.cs` | depends on `IMqttConnector`, honest bool | 2 |
| `src/api/Interfaces/IMqttConnector.cs` | adds `Task<bool> PublishAsync` | 2 |
| `src/api/HostedServices/MqttConnector.cs` | `PublishAsync` returns bool, reconnect guarded | 2 |
| `src/api/Services/AwtrixService.cs` | `SafePublish`, optional logger; publisher-built custom URL (T3) | 2, 3 |
| `src/api/Program.cs` | named HttpClient + `IMqttConnector` registration (T2); `AddAwtrixServices` extraction (T5) | 2, 5 |
| `test/Test/Services/StubHttp.cs` | `StubHttpMessageHandler`, `StubHttpClientFactory` | 2 |
| `test/Test/Services/HttpPublisherTests.cs` | HttpPublisher behaviour | 2, 3 |
| `test/Test/Services/MqttPublisherTests.cs` | MqttPublisher behaviour | 2 |
| `test/Test/Services/FakePublishers.cs` | new HttpPublisher ctor; `ThrowOnPublish` | 2 |
| `test/Test/Services/AwtrixServicePublishTests.cs` | throwing publisher; HTTP custom URLs (T3) | 2, 3 |
| `test/Test/HostedServices/ConductorTestHelper.cs` | new HttpPublisher ctor (T2); interface mocks (T5) | 2, 5 |
| `src/api/Domain/AwtrixAddress.cs` | `IsHttp`, `IsHttpTopic` | 3 |
| `test/Test/Domain/AwtrixAddressTests.cs`, `test/Test/Services/AwtrixPublisherTests.cs` | new assertions | 3 |
| `src/api/Apps/AwtrixApp.cs` | `FireAndLog` | 4 |
| `src/api/Domain/AwtrixSettings.cs` | safe `ToString` | 4 |
| `src/api/Apps/Diurnal/DiurnalApp.cs` | `IClock`, FireAndLog, skip empty, minute truncation | 4 |
| `src/api/Apps/TripTimer/TripTimerApp.cs` | FireAndLog in `ClockTickSecond` | 4 |
| `src/api/Apps/MqttRender/MqttClockRenderApp.cs` | FireAndLog in `ClockTick` | 4 |
| `src/api/HostedServices/Conductor.cs` | pass clock to DiurnalApp (T4); interface ctor, HTTP ButtonApp skip, safe StopAsync (T5) | 4, 5 |
| `test/Test/Apps/Diurnal/DiurnalAppTests.cs`, `test/Test/Apps/TripTimer/TripTimerAppTickTests.cs`, `test/Test/Domain/AwtrixSettingsTests.cs` | tick-safety tests | 4 |
| `src/api/Interfaces/ISlackConnector.cs` | new seam | 5 |
| `src/api/HostedServices/SlackConnector.cs`, `src/api/Apps/SlackStatus/SlackStatusApp.cs`, `src/api/Services/IAwtrixService.cs`, `src/api/Controllers/DiagnosticsController.cs`, `src/api/Domain/Clock.cs` | seam adoption | 5 |
| `test/Test/HostedServices/ConductorTests.cs`, `test/Test/CompositionRootTests.cs` | StartAsync/StopAsync and DI tests | 5 |

---

### Task 1: TimeProvider-driven, non-overlapping TimerService with isolated subscribers (CR-11, CR-01 timer side)

**Files:**
- Modify: `test/Test/Test.csproj` (add package)
- Modify (full rewrite): `src/api/HostedServices/TimerService.cs`
- Modify (full rewrite): `test/Test/HostedServices/TimerServiceEventTests.cs`. It replaces the reflection tests on `_lastTime` / `CheckTimeChange`, which no longer exist. `Dispose_DoesNotThrow_WhenTimerNeverStarted` and `StartAsync_ThenStopAsync_CompletesWithoutHanging` are kept.

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `public TimerService(ILogger<TimerService> logger, TimeProvider? timeProvider = null)`
  - `internal void Tick()`
  - `internal static readonly TimeSpan TickInterval` (100 ms)
  - `ClockTickEventArgs.Time` contract (local, truncated to second, `Kind=Local`)
  - `ITimerService` unchanged; `TimerService.FormatClockString` unchanged

- [ ] **Step 1: Add the FakeTimeProvider package**

Run: `dotnet add test/Test/Test.csproj package Microsoft.Extensions.TimeProvider.Testing --version 10.0.0`
Expected: `PackageReference for package 'Microsoft.Extensions.TimeProvider.Testing' version '10.0.0' added`

- [ ] **Step 2: Write the failing tests (replace the whole file)**

`test/Test/HostedServices/TimerServiceEventTests.cs`:

```csharp
using AwtrixSharpWeb.HostedServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Test.HostedServices
{
    /// <summary>
    /// Drives TimerService deterministically through an injected FakeTimeProvider.
    /// Most tests call the internal Tick() directly; one test exercises the real
    /// PeriodicTimer loop via StartAsync.
    /// </summary>
    public class TimerServiceEventTests
    {
        // 06:59:58.000 UTC - two seconds before a minute boundary
        private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 9, 13, 6, 59, 58, TimeSpan.Zero);

        private static (TimerService service, FakeTimeProvider time) CreateService(TimeZoneInfo? localZone = null)
        {
            var time = new FakeTimeProvider(Start);
            time.SetLocalTimeZone(localZone ?? TimeZoneInfo.Utc);
            var service = new TimerService(NullLogger<TimerService>.Instance, time);
            return (service, time);
        }

        [Fact]
        public void Tick_WithinSameSecond_RaisesNothing()
        {
            var (service, time) = CreateService();
            var seconds = 0;
            var minutes = 0;
            service.SecondChanged += (_, _) => seconds++;
            service.MinuteChanged += (_, _) => minutes++;

            time.Advance(TimeSpan.FromMilliseconds(500));
            service.Tick();

            Assert.Equal(0, seconds);
            Assert.Equal(0, minutes);
        }

        [Fact]
        public void Tick_NextSecond_RaisesSecondChangedWithTruncatedLocalTime_AndNoMinute()
        {
            var (service, time) = CreateService();
            ClockTickEventArgs? raised = null;
            var minutes = 0;
            service.SecondChanged += (_, e) => raised = e;
            service.MinuteChanged += (_, _) => minutes++;

            time.Advance(TimeSpan.FromMilliseconds(1300));
            service.Tick();

            Assert.NotNull(raised);
            Assert.Equal(new DateTime(2026, 9, 13, 6, 59, 59), raised!.Time);
            Assert.Equal(DateTimeKind.Local, raised.Time.Kind);
            Assert.Equal(0, minutes);
        }

        [Fact]
        public void Tick_CrossingMinuteBoundary_RaisesBothEvents()
        {
            var (service, time) = CreateService();
            DateTime? secondTime = null;
            DateTime? minuteTime = null;
            service.SecondChanged += (_, e) => secondTime = e.Time;
            service.MinuteChanged += (_, e) => minuteTime = e.Time;

            time.Advance(TimeSpan.FromSeconds(2));
            service.Tick();

            Assert.Equal(new DateTime(2026, 9, 13, 7, 0, 0), secondTime);
            Assert.Equal(new DateTime(2026, 9, 13, 7, 0, 0), minuteTime);
        }

        [Fact]
        public void Tick_CalledTwiceInSameSecond_RaisesSecondChangedOnce()
        {
            var (service, time) = CreateService();
            var seconds = 0;
            service.SecondChanged += (_, _) => seconds++;

            time.Advance(TimeSpan.FromSeconds(1));
            service.Tick();
            time.Advance(TimeSpan.FromMilliseconds(100));
            service.Tick();

            Assert.Equal(1, seconds);
        }

        [Fact]
        public void Tick_AfterStallOfExactlyWholeMinutes_StillRaisesBothEvents()
        {
            // Old implementation compared only .Second/.Minute fields and missed this case.
            var (service, time) = CreateService();
            var seconds = 0;
            var minutes = 0;
            service.SecondChanged += (_, _) => seconds++;
            service.MinuteChanged += (_, _) => minutes++;

            time.Advance(TimeSpan.FromMinutes(60));
            service.Tick();

            Assert.Equal(1, seconds);
            Assert.Equal(1, minutes);
        }

        [Fact]
        public void Tick_WhenASubscriberThrows_OtherSubscribersAndMinuteEventStillRun()
        {
            var (service, time) = CreateService();
            var laterSecondSubscriberCalled = false;
            var minuteSubscriberCalled = false;
            service.SecondChanged += (_, _) => throw new InvalidOperationException("boom");
            service.SecondChanged += (_, _) => laterSecondSubscriberCalled = true;
            service.MinuteChanged += (_, _) => throw new OverflowException("boom");
            service.MinuteChanged += (_, _) => minuteSubscriberCalled = true;

            time.Advance(TimeSpan.FromSeconds(2));
            var exception = Record.Exception(() => service.Tick());

            Assert.Null(exception);
            Assert.True(laterSecondSubscriberCalled);
            Assert.True(minuteSubscriberCalled);
        }

        [Fact]
        public void Tick_UsesTimeProviderLocalTimeZone()
        {
            var aest = TimeZoneInfo.CreateCustomTimeZone("Test+10", TimeSpan.FromHours(10), "Test+10", "Test+10");
            var (service, time) = CreateService(aest);
            DateTime? raised = null;
            service.SecondChanged += (_, e) => raised = e.Time;

            time.Advance(TimeSpan.FromSeconds(1));
            service.Tick();

            Assert.Equal(new DateTime(2026, 9, 13, 16, 59, 59), raised);
        }

        [Fact]
        public async Task StartAsync_DrivesTicksFromInjectedTimeProvider()
        {
            var (service, time) = CreateService();
            var raised = new TaskCompletionSource<DateTime>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.SecondChanged += (_, e) => raised.TrySetResult(e.Time);

            await service.StartAsync(CancellationToken.None);
            for (var i = 0; i < 200 && !raised.Task.IsCompleted; i++)
            {
                time.Advance(TimeSpan.FromMilliseconds(200));
                await Task.Delay(10);
            }
            await service.StopAsync(CancellationToken.None);
            service.Dispose();

            Assert.True(raised.Task.IsCompleted, "SecondChanged was never raised by the PeriodicTimer loop");
        }

        [Fact]
        public void Dispose_DoesNotThrow_WhenTimerNeverStarted()
        {
            var service = new TimerService(NullLogger<TimerService>.Instance);

            var exception = Record.Exception(() => service.Dispose());

            Assert.Null(exception);
        }

        [Fact]
        public async Task StartAsync_ThenStopAsync_CompletesWithoutHanging()
        {
            var service = new TimerService(NullLogger<TimerService>.Instance);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            await service.StartAsync(cts.Token);
            await service.StopAsync(CancellationToken.None);

            service.Dispose();
        }
    }
}
```

- [ ] **Step 3: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.HostedServices.TimerServiceEventTests"`
Expected: build FAILS with `CS1729: 'TimerService' does not contain a constructor that takes 2 arguments` and `CS1061: 'TimerService' does not contain a definition for 'Tick'`.

- [ ] **Step 4: Implement (replace the whole file)**

`src/api/HostedServices/TimerService.cs`:

```csharp
using AwtrixSharpWeb.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AwtrixSharpWeb.HostedServices
{
    public class ClockTickEventArgs : EventArgs
    {
        /// <summary>
        /// Local wall-clock time of the tick, truncated to the whole second (Kind = Local).
        /// </summary>
        public DateTime Time { get; }

        public ClockTickEventArgs(DateTime currentTime)
        {
            Time = currentTime;
        }
    }

    /// <summary>
    /// Raises SecondChanged / MinuteChanged from a single sequential PeriodicTimer loop.
    /// Subscribers are invoked one at a time, each isolated in its own try/catch, so a
    /// throwing subscriber can neither crash the process nor starve other subscribers.
    /// Subscribers must return quickly; offload I/O (see AwtrixApp.FireAndLog).
    /// </summary>
    public class TimerService : IHostedService, IDisposable, ITimerService
    {
        internal static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);

        private readonly ILogger<TimerService> _logger;
        private readonly TimeProvider _timeProvider;
        private DateTime _lastSecond;
        private Task? _executingTask;
        private CancellationTokenSource? _stoppingCts;

        /// <summary>
        /// Event that fires every second
        /// </summary>
        public event EventHandler<ClockTickEventArgs>? SecondChanged;

        /// <summary>
        /// Event that fires every minute
        /// </summary>
        public event EventHandler<ClockTickEventArgs>? MinuteChanged;

        public TimerService(ILogger<TimerService> logger, TimeProvider? timeProvider = null)
        {
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _lastSecond = CurrentLocalSecond();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _executingTask = ExecuteAsync(_stoppingCts.Token);
            return _executingTask.IsCompleted ? _executingTask : Task.CompletedTask;
        }

        private async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogDebug("Timer service executing");
            using var timer = new PeriodicTimer(TickInterval, _timeProvider);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    Tick();
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Timer service stopping due to cancellation");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Timer loop terminated unexpectedly");
            }
            finally
            {
                _logger.LogInformation("Timer service stopped");
            }
        }

        /// <summary>
        /// One timer iteration. Never throws.
        /// </summary>
        internal void Tick()
        {
            try
            {
                var current = CurrentLocalSecond();
                if (current == _lastSecond)
                {
                    return;
                }

                var previous = _lastSecond;
                // Update before invoking handlers so a slow/throwing handler can't cause a duplicate
                _lastSecond = current;

                _logger.LogDebug("Second changed: {Second}", current.ToString("HH:mm:ss"));
                Raise(SecondChanged, current, nameof(SecondChanged));

                if (TruncateToMinute(current) != TruncateToMinute(previous))
                {
                    _logger.LogDebug("Minute changed: {Minute}", current.ToString("HH:mm:ss"));
                    Raise(MinuteChanged, current, nameof(MinuteChanged));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Timer tick failed");
            }
        }

        private void Raise(EventHandler<ClockTickEventArgs>? handlers, DateTime time, string eventName)
        {
            if (handlers == null)
            {
                return;
            }

            var args = new ClockTickEventArgs(time);
            foreach (EventHandler<ClockTickEventArgs> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, args);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "{Event} subscriber {Subscriber} threw; continuing with remaining subscribers",
                        eventName,
                        $"{handler.Method.DeclaringType?.Name}.{handler.Method.Name}");
                }
            }
        }

        private DateTime CurrentLocalSecond()
        {
            var local = _timeProvider.GetLocalNow().DateTime;
            return new DateTime(local.Ticks - (local.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Local);
        }

        private static DateTime TruncateToMinute(DateTime time)
        {
            return new DateTime(time.Ticks - (time.Ticks % TimeSpan.TicksPerMinute), time.Kind);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping timer service");

            if (_executingTask == null)
            {
                return;
            }

            try
            {
                _stoppingCts?.Cancel();
            }
            finally
            {
                var completedTask = await Task.WhenAny(_executingTask, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));

                if (completedTask != _executingTask)
                {
                    _logger.LogWarning("Timer service shutdown timed out");
                }
            }
        }

        public void Dispose()
        {
            _stoppingCts?.Dispose();
        }

        public static string FormatClockString(DateTime time, bool format24h)
        {
            var thisSecond = time.Second;
            var isOddSecond = thisSecond % 2 == 1;
            var spacer = isOddSecond ? " " : ":";

            var hourString = time.ToString(format24h ? "HH" : "hh");
            if (!format24h)
            {
                hourString = hourString.TrimStart('0');
            }

            var clockText = $"{hourString}{spacer}{time:mm}";

            return clockText;
        }
    }
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.HostedServices.TimerServiceEventTests|FullyQualifiedName~Test.Services.TimerServiceTests"`
Expected: PASS, 12 tests (10 in TimerServiceEventTests + 2 in TimerServiceTests), 0 failed.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test`
Expected: 0 failed. `TripTimerAppTests`, `ConductorTests` and the other suites still pass, because `new TimerService(logger)` still compiles.

- [ ] **Step 7: Commit**

```bash
git add test/Test/Test.csproj src/api/HostedServices/TimerService.cs test/Test/HostedServices/TimerServiceEventTests.cs
git commit -m "fix(timer): non-overlapping TimeProvider tick loop with isolated subscribers

CR-11, CR-01 (timer side): replace System.Threading.Timer callback with a
sequential PeriodicTimer loop over an injected TimeProvider; compare whole
truncated DateTimes; update last-second before invoking handlers; invoke
each subscriber in its own try/catch.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 2: Publishers never throw and return honest bools (CR-02)

**Files:**
- Create: `test/Test/Services/StubHttp.cs`
- Create: `test/Test/Services/HttpPublisherTests.cs`
- Create: `test/Test/Services/MqttPublisherTests.cs`
- Modify (full rewrite): `src/api/Services/AwtrixPublisher.cs`, `src/api/Services/HttpPublisher.cs`, `src/api/Services/MqttPublisher.cs`, `src/api/Interfaces/IMqttConnector.cs`, `src/api/Services/AwtrixService.cs`
- Modify: `src/api/HostedServices/MqttConnector.cs` (the `PublishAsync` method only)
- Modify: `src/api/Program.cs` (two registrations plus one using)
- Modify: `test/Test/Services/FakePublishers.cs` (new base constructor; `ThrowOnPublish`)
- Modify: `test/Test/Services/AwtrixServicePublishTests.cs` (add one test)
- Modify: `test/Test/HostedServices/ConductorTestHelper.cs` (new HttpPublisher constructor)

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces:
  - `AwtrixPublisher`: `protected ILogger Logger { get; }`; `public abstract Task<bool> Publish(string url, string payload)` (contract: never throws)
  - `public HttpPublisher(ILogger<HttpPublisher> logger, IHttpClientFactory httpClientFactory)`
  - `public const string HttpPublisher.HttpClientName = "AwtrixHttpPublisher"`
  - `public static readonly TimeSpan HttpPublisher.DefaultTimeout` (5 s)
  - `public MqttPublisher(IMqttConnector mqttConnector, ILogger<MqttPublisher> logger)`
  - `IMqttConnector.PublishAsync(string topic, string payload) : Task<bool>`; `MqttConnector.PublishAsync` returns `Task<bool>`
  - `public AwtrixService(HttpPublisher httpPublisher, MqttPublisher mqttPublisher, ILogger<AwtrixService>? logger = null)`
  - Test helpers: `StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>)`, `StubHttpMessageHandler.Returning(HttpStatusCode)`, `.Requests`, `.RequestBodies`; `StubHttpClientFactory(HttpMessageHandler handler, TimeSpan? timeout = null)`, `.RequestedNames`
  - `FakeMqttPublisher.ThrowOnPublish` and `FakeHttpPublisher.ThrowOnPublish` (`Exception?`)

- [ ] **Step 1: Create the HTTP test doubles**

`test/Test/Services/StubHttp.cs`:

```csharp
using System.Net;

namespace Test.Services
{
    /// <summary>
    /// HttpMessageHandler whose behaviour is supplied per test. Records requests and bodies.
    /// </summary>
    public sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> RequestBodies { get; } = new();

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public static StubHttpMessageHandler Returning(HttpStatusCode status)
        {
            return new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return await _handler(request, cancellationToken);
        }
    }

    /// <summary>
    /// IHttpClientFactory returning clients over a shared stub handler.
    /// </summary>
    public sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        private readonly TimeSpan _timeout;

        public List<string> RequestedNames { get; } = new();

        public StubHttpClientFactory(HttpMessageHandler handler, TimeSpan? timeout = null)
        {
            _handler = handler;
            _timeout = timeout ?? TimeSpan.FromSeconds(5);
        }

        public HttpClient CreateClient(string name)
        {
            RequestedNames.Add(name);
            return new HttpClient(_handler, disposeHandler: false) { Timeout = _timeout };
        }
    }
}
```

- [ ] **Step 2: Write the failing HttpPublisher tests**

`test/Test/Services/HttpPublisherTests.cs`:

```csharp
using System.Net;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Test.Services
{
    public class HttpPublisherTests
    {
        private static HttpPublisher CreatePublisher(StubHttpMessageHandler handler, out StubHttpClientFactory factory, TimeSpan? timeout = null)
        {
            factory = new StubHttpClientFactory(handler, timeout);
            return new HttpPublisher(NullLogger<HttpPublisher>.Instance, factory);
        }

        [Fact]
        public async Task Publish_Success_ReturnsTrue_AndPostsJsonToUrlUsingNamedClient()
        {
            var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK);
            var publisher = CreatePublisher(handler, out var factory);

            var result = await publisher.Publish("http://192.168.1.50/api/notify", "{\"text\":\"hi\"}");

            Assert.True(result);
            var request = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://192.168.1.50/api/notify", request.RequestUri!.ToString());
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            Assert.Equal("{\"text\":\"hi\"}", handler.RequestBodies[0]);
            Assert.Equal(new[] { HttpPublisher.HttpClientName }, factory.RequestedNames);
        }

        [Fact]
        public async Task Publish_NonSuccessStatus_ReturnsFalse()
        {
            var publisher = CreatePublisher(StubHttpMessageHandler.Returning(HttpStatusCode.NotFound), out _);

            var result = await publisher.Publish("http://192.168.1.50/api/custom/TripTimerApp", "{}");

            Assert.False(result);
        }

        [Fact]
        public async Task Publish_WhenConnectionFails_ReturnsFalseWithoutThrowing()
        {
            var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("No route to host"));
            var publisher = CreatePublisher(handler, out _);

            var result = await publisher.Publish("http://192.168.1.50/api/notify", "{}");

            Assert.False(result);
        }

        [Fact]
        public async Task Publish_WhenRequestTimesOut_ReturnsFalseWithoutThrowing()
        {
            var handler = new StubHttpMessageHandler(async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            var publisher = CreatePublisher(handler, out _, TimeSpan.FromMilliseconds(100));

            var result = await publisher.Publish("http://192.168.1.50/api/notify", "{}");

            Assert.False(result);
        }

        [Fact]
        public async Task Publish_WithRelativeUrl_ReturnsFalseWithoutThrowing()
        {
            var publisher = CreatePublisher(StubHttpMessageHandler.Returning(HttpStatusCode.OK), out _);

            var result = await publisher.Publish("awtrix/clock1/notify", "{}");

            Assert.False(result);
        }

        [Fact]
        public async Task Publish_DisposesResponse()
        {
            var content = new TrackingContent();
            var handler = new StubHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
            var publisher = CreatePublisher(handler, out _);

            await publisher.Publish("http://192.168.1.50/api/notify", "{}");

            Assert.True(content.Disposed);
        }

        [Fact]
        public void DefaultTimeout_IsFiveSeconds()
        {
            Assert.Equal(TimeSpan.FromSeconds(5), HttpPublisher.DefaultTimeout);
        }

        private sealed class TrackingContent : StringContent
        {
            public bool Disposed { get; private set; }

            public TrackingContent() : base(string.Empty)
            {
            }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
```

- [ ] **Step 3: Write the failing MqttPublisher tests**

`test/Test/Services/MqttPublisherTests.cs`:

```csharp
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Test.Services
{
    public class MqttPublisherTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Publish_ReturnsConnectorResult(bool connectorResult)
        {
            var connector = new Mock<IMqttConnector>();
            connector.Setup(c => c.PublishAsync("awtrix/clock1/notify", "{}")).ReturnsAsync(connectorResult);
            var publisher = new MqttPublisher(connector.Object, NullLogger<MqttPublisher>.Instance);

            var result = await publisher.Publish("awtrix/clock1/notify", "{}");

            Assert.Equal(connectorResult, result);
        }

        [Fact]
        public async Task Publish_WhenConnectorThrows_ReturnsFalseWithoutThrowing()
        {
            var connector = new Mock<IMqttConnector>();
            connector.Setup(c => c.PublishAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("client not connected"));
            var publisher = new MqttPublisher(connector.Object, NullLogger<MqttPublisher>.Instance);

            var result = await publisher.Publish("awtrix/clock1/notify", "{}");

            Assert.False(result);
        }
    }
}
```

- [ ] **Step 4: Add the throwing-publisher test to AwtrixServicePublishTests**

Append this method inside `public class AwtrixServicePublishTests` in `test/Test/Services/AwtrixServicePublishTests.cs`, after `FailedPublish_PropagatesFalseResult`:

```csharp
        [Fact]
        public async Task PublisherThatThrows_IsContainedAndReportedAsFalse()
        {
            var (service, _, mqtt) = CreateService();
            mqtt.ThrowOnPublish = new InvalidOperationException("contract violation");
            var address = new AwtrixAddress { BaseTopic = "awtrix/clock1" };

            var notify = await service.Notify(address, new AwtrixAppMessage().SetText("hi"));
            var update = await service.AppUpdate(address, "MyApp", new AwtrixAppMessage().SetText("42"));
            var set = await service.Set(address, new AwtrixSettings().SetBrightness(5));

            Assert.False(notify);
            Assert.False(update);
            Assert.False(set);
        }
```

- [ ] **Step 5: Update the test fakes (replace the whole file)**

`test/Test/Services/FakePublishers.cs`:

```csharp
using System.Net;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AwtrixSharpWeb.Domain;

namespace Test.Services
{
    /// <summary>
    /// Test double for HttpPublisher that captures the last published url/payload
    /// instead of making a real HTTP call. Overrides the abstract Publish(url, payload)
    /// so the stub IHttpClientFactory passed to the base is never used.
    /// </summary>
    public class FakeHttpPublisher : HttpPublisher
    {
        public string? LastUrl { get; private set; }
        public string? LastPayload { get; private set; }
        public int PublishCallCount { get; private set; }
        public bool ReturnValue { get; set; } = true;
        public Exception? ThrowOnPublish { get; set; }

        public FakeHttpPublisher()
            : base(NullLogger<HttpPublisher>.Instance, new StubHttpClientFactory(StubHttpMessageHandler.Returning(HttpStatusCode.OK)))
        {
        }

        public override Task<bool> Publish(string url, string payload)
        {
            LastUrl = url;
            LastPayload = payload;
            PublishCallCount++;
            if (ThrowOnPublish != null)
            {
                throw ThrowOnPublish;
            }
            return Task.FromResult(ReturnValue);
        }
    }

    /// <summary>
    /// Test double for MqttPublisher that captures the last published topic/payload
    /// instead of routing through a real MqttConnector/broker.
    /// </summary>
    public class FakeMqttPublisher : MqttPublisher
    {
        public string? LastUrl { get; private set; }
        public string? LastPayload { get; private set; }
        public int PublishCallCount { get; private set; }
        public bool ReturnValue { get; set; } = true;
        public Exception? ThrowOnPublish { get; set; }

        public FakeMqttPublisher()
            : base(new MqttConnector(NullLogger<MqttConnector>.Instance, Options.Create(new MqttSettings())), NullLogger<MqttPublisher>.Instance)
        {
        }

        public override Task<bool> Publish(string url, string payload)
        {
            LastUrl = url;
            LastPayload = payload;
            PublishCallCount++;
            if (ThrowOnPublish != null)
            {
                throw ThrowOnPublish;
            }
            return Task.FromResult(ReturnValue);
        }
    }
}
```

- [ ] **Step 6: Run the new tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Services.HttpPublisherTests|FullyQualifiedName~Test.Services.MqttPublisherTests|FullyQualifiedName~Test.Services.AwtrixServicePublishTests"`
Expected: build FAILS with `CS1729: 'HttpPublisher' does not contain a constructor that takes 2 arguments`, and `CS1061` / `CS1503` on `IMqttConnector.PublishAsync` / `MqttPublisher(IMqttConnector, ...)`.

- [ ] **Step 7: Implement AwtrixPublisher (replace the whole file)**

`src/api/Services/AwtrixPublisher.cs`:

```csharp
using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Services
{
    public abstract class AwtrixPublisher
    {
        protected ILogger Logger { get; }

        protected AwtrixPublisher(ILogger logger)
        {
            Logger = logger;
        }

        public string ToJson(AwtrixAppMessage? message)
        {
            if (message == null)
            {
                return string.Empty;
            }
            else
            {
                return message.ToJson();
            }
        }

        /// <summary>
        /// Transport primitive. Contract: MUST NOT throw. Returns true only when the transport
        /// confirmed the hand-off (HTTP 2xx / MQTT client publish completed); every failure is
        /// logged by the implementation and reported as false.
        /// </summary>
        public abstract Task<bool> Publish(string url, string payload);

        public async Task<bool> Publish(string url, AwtrixAppMessage? message)
        {
            var json = ToJson(message);
            var publisherType = this.GetType().Name;
            Logger.LogDebug("{publisherType} Publishing to {url} with payload: {json}", publisherType, url, json);
            return await Publish(url, json);
        }
    }
}
```

- [ ] **Step 8: Implement HttpPublisher (replace the whole file)**

`src/api/Services/HttpPublisher.cs`:

```csharp
using System.Text;

namespace AwtrixSharpWeb.Services
{
    /// <summary>
    /// Publishes to an Awtrix device's HTTP API. Never throws: every failure is logged and reported as false.
    /// </summary>
    public class HttpPublisher : AwtrixPublisher
    {
        public const string HttpClientName = "AwtrixHttpPublisher";

        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

        private readonly IHttpClientFactory _httpClientFactory;

        public HttpPublisher(ILogger<HttpPublisher> logger, IHttpClientFactory httpClientFactory) : base(logger)
        {
            _httpClientFactory = httpClientFactory;
        }

        public override async Task<bool> Publish(string url, string payload)
        {
            try
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                using var content = new StringContent(payload ?? string.Empty, Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                Logger.LogWarning("HTTP publish to {Url} returned {StatusCode}", url, (int)response.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                // Routine when a device is offline: log type + message, not the stack trace
                Logger.LogWarning("HTTP publish to {Url} failed: {ErrorType}: {Error}", url, ex.GetType().Name, ex.Message);
                return false;
            }
        }
    }
}
```

- [ ] **Step 9: Implement IMqttConnector, MqttPublisher and MqttConnector.PublishAsync**

`src/api/Interfaces/IMqttConnector.cs` (replace the whole file):

```csharp
using MQTTnet;

namespace AwtrixSharpWeb.Interfaces
{
    public interface IMqttConnector
    {
        event Func<MqttApplicationMessageReceivedEventArgs, Task> MessageReceived;

        Task Subscribe(string topic);

        /// <summary>
        /// Returns true when the client accepted the publish; false (never throws) otherwise.
        /// </summary>
        Task<bool> PublishAsync(string topic, string payload);
    }
}
```

`src/api/Services/MqttPublisher.cs` (replace the whole file):

```csharp
using AwtrixSharpWeb.Interfaces;

namespace AwtrixSharpWeb.Services
{
    /// <summary>
    /// Publishes to an Awtrix device over MQTT. Never throws: failures are reported as false.
    /// </summary>
    public class MqttPublisher : AwtrixPublisher
    {
        private readonly IMqttConnector _mqttConnector;

        public MqttPublisher(IMqttConnector mqttConnector, ILogger<MqttPublisher> logger) : base(logger)
        {
            _mqttConnector = mqttConnector;
        }

        public override async Task<bool> Publish(string topic, string payload)
        {
            try
            {
                return await _mqttConnector.PublishAsync(topic, payload);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "MQTT publish to {Topic} failed", topic);
                return false;
            }
        }
    }
}
```

In `src/api/HostedServices/MqttConnector.cs`, replace the whole `public async Task PublishAsync(string topic, string payload)` method (lines 85-108) with:

```csharp
        public async Task<bool> PublishAsync(string topic, string payload)
        {
            payload ??= string.Empty;
            var payloadLog = payload.Length == 0 ? "<empty>" : payload;
            _log.LogDebug("Publishing MQTT to topic {Topic} with payload {Payload}", topic, payloadLog);

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(Encoding.UTF8.GetBytes(payload))
                .Build();

            try
            {
                await _client.PublishAsync(message, CancellationToken.None);
                return true;
            }
            catch (Exception e)
            {
                _log.LogError("Failed to publish MQTT message to {Topic} ({Error})", topic, e.Message);

                // Reconnect-on-failure is preserved until WS2 replaces it; it must never escape.
                try
                {
                    await ConnectAsync();
                }
                catch (Exception reconnectEx)
                {
                    _log.LogError(reconnectEx, "MQTT reconnect after failed publish also failed");
                }

                return false;
            }
        }
```

- [ ] **Step 10: Implement AwtrixService (replace the whole file)**

`src/api/Services/AwtrixService.cs`:

```csharp
using AwtrixSharpWeb.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace AwtrixSharpWeb.Services
{
    public class AwtrixService : IAwtrixService
    {
        private readonly HttpPublisher _httpPublisher;
        private readonly MqttPublisher _mqttPublisher;
        private readonly ILogger _logger;

        public AwtrixService(HttpPublisher httpPublisher, MqttPublisher mqttPublisher, ILogger<AwtrixService>? logger = null)
        {
            _httpPublisher = httpPublisher;
            _mqttPublisher = mqttPublisher;
            _logger = (ILogger?)logger ?? NullLogger.Instance;
        }

        /// <summary>
        /// https://blueforcer.github.io/awtrix3/#/api?id=change-settings
        /// </summary>
        public Task<bool> Set(AwtrixAddress awtrixAddress, AwtrixSettings settings)
        {
            var baseTopic = awtrixAddress.BaseTopic;
            var payload = settings.ToJson();
            return SafePublish(baseTopic, p => p.Publish(baseTopic + "/settings", payload));
        }

        /// <summary>
        /// https://blueforcer.github.io/awtrix3/#/api?id=sound-playback
        /// </summary>
        public Task<bool> PlayRtttl(AwtrixAddress awtrixAddress, string rtttl)
        {
            var baseTopic = awtrixAddress.BaseTopic;
            return SafePublish(baseTopic, p => p.Publish(baseTopic + "/rtttl", rtttl));
        }

        public Task<bool> AppUpdate(AwtrixAddress awtrixAddress, string appName, AwtrixAppMessage message)
        {
            var baseTopic = awtrixAddress.BaseTopic;
            return SafePublish(baseTopic, p => p.Publish(baseTopic + $"/custom/{appName}", message));
        }

        public Task<bool> AppClear(AwtrixAddress awtrixAddress, string appName)
        {
            var baseTopic = awtrixAddress.BaseTopic;
            return SafePublish(baseTopic, p => p.Publish(baseTopic + $"/custom/{appName}", (AwtrixAppMessage?)null));
        }

        public Task<bool> Notify(AwtrixAddress awtrixAddress, AwtrixAppMessage message)
        {
            if (String.IsNullOrWhiteSpace(message.Text))
            {
                return Dismiss(awtrixAddress);
            }

            var baseTopic = awtrixAddress.BaseTopic;
            return SafePublish(baseTopic, p => p.Publish(baseTopic + "/notify", message));
        }

        /// <summary>
        /// Blink acts as a sign of life
        /// </summary>
        public static (int quantized, int quantizedBlink) Quantize(int progress)
        {
            int p = Math.Clamp(progress, 0, 100);

            int LedCount(int v)
            {
                if (v < 4) return 0;
                if (v == 100) return 32;
                return 1 + (int)Math.Floor((v - 4) * 31.0 / 96.0); // 4–99 => 1–31
            }

            int n = LedCount(p);
            int blink;

            if (n == 0) blink = 4;            // nothing lit
            else if (n == 32) blink = 99;     // drop to 31 LEDs
            else if (p < 4) blink = 1; // one bin lower
            else
            {
                int lowerBound = (n == 1) ? 4 : (int)Math.Ceiling(4 + 96.0 * (n - 1) / 31.0);
                blink = Math.Clamp(lowerBound - 1, 0, 99); // one bin lower
            }

            return (p, blink);
        }

        /// <remarks>https://blueforcer.github.io/awtrix3/#/api?id=dismiss-notification</remarks>
        public Task<bool> Dismiss(AwtrixAddress awtrixAddress)
        {
            var baseTopic = awtrixAddress.BaseTopic;
            return SafePublish(baseTopic, p => p.Publish(baseTopic + "/notify/dismiss", (AwtrixAppMessage?)null));
        }

        /// <summary>
        /// Defensive wrapper: publishers must not throw, but if one does the failure is contained here.
        /// Publishers log the failure reason themselves, so a plain false is only logged at Debug.
        /// </summary>
        private async Task<bool> SafePublish(string baseTopic, Func<AwtrixPublisher, Task<bool>> publish)
        {
            try
            {
                var publisher = ResolvePublisher(baseTopic);
                var delivered = await publish(publisher);
                if (!delivered)
                {
                    _logger.LogDebug("Publish via {Publisher} for {BaseTopic} was not delivered", publisher.GetType().Name, baseTopic);
                }
                return delivered;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Publisher threw for {BaseTopic}; treating as not delivered", baseTopic);
                return false;
            }
        }

        private AwtrixPublisher ResolvePublisher(string topic)
        {
            if (topic.StartsWith("http://") || topic.StartsWith("https://"))
            {
                return _httpPublisher;
            }
            else
            {
                return _mqttPublisher;
            }
        }
    }
}
```

- [ ] **Step 11: Update ConductorTestHelper for the new HttpPublisher constructor**

In `test/Test/HostedServices/ConductorTestHelper.cs`:

Add `using System.Net;` and `using Test.Services;` to the using block. Then replace

```csharp
            var httpPublisher = new HttpPublisher(NullLogger<HttpPublisher>.Instance);
```

with

```csharp
            var httpPublisher = new HttpPublisher(
                NullLogger<HttpPublisher>.Instance,
                new StubHttpClientFactory(StubHttpMessageHandler.Returning(HttpStatusCode.OK)));
```

- [ ] **Step 12: Register the named client and IMqttConnector in Program.cs**

In `src/api/Program.cs`, add `using AwtrixSharpWeb.Interfaces;` to the using block. Then replace

```csharp
            services.AddSingleton<MqttConnector>();
```

with

```csharp
            services.AddSingleton<MqttConnector>();
            services.AddSingleton<IMqttConnector>(sp => sp.GetRequiredService<MqttConnector>());
            services.AddHttpClient(HttpPublisher.HttpClientName, client => client.Timeout = HttpPublisher.DefaultTimeout);
```

- [ ] **Step 13: Run the targeted tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Services|FullyQualifiedName~Test.HostedServices.MqttPublisherConnectorTests|FullyQualifiedName~Test.Controllers.DiagnosticsControllerTests"`
Expected: PASS, 0 failed. `MqttPublisherConnectorTests.MqttPublisher_Publish_ReturnsTrue` still passes, because the mocked client publish completes and `PublishAsync` returns true.

- [ ] **Step 14: Run the full suite**

Run: `dotnet test`
Expected: 0 failed.

- [ ] **Step 15: Commit**

```bash
git add test/Test/Services/StubHttp.cs test/Test/Services/HttpPublisherTests.cs test/Test/Services/MqttPublisherTests.cs test/Test/Services/FakePublishers.cs test/Test/Services/AwtrixServicePublishTests.cs test/Test/HostedServices/ConductorTestHelper.cs src/api/Services/AwtrixPublisher.cs src/api/Services/HttpPublisher.cs src/api/Services/MqttPublisher.cs src/api/Services/AwtrixService.cs src/api/Interfaces/IMqttConnector.cs src/api/HostedServices/MqttConnector.cs src/api/Program.cs
git commit -m "fix(publish): publishers never throw and return honest results

CR-02: HttpPublisher uses a named IHttpClientFactory client with a 5 s
timeout, disposes request/response and maps every failure to false.
MqttPublisher depends on IMqttConnector and returns its bool result.
AwtrixService contains any publisher exception via SafePublish.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 3: HTTP custom-app URLs built by the publisher (CR-12, transport paths)

**Files:**
- Modify: `src/api/Domain/AwtrixAddress.cs` (full rewrite)
- Modify: `src/api/Services/AwtrixPublisher.cs` (add a method)
- Modify: `src/api/Services/HttpPublisher.cs` (add an override)
- Modify: `src/api/Services/AwtrixService.cs` (`AppUpdate`, `AppClear`, `ResolvePublisher`)
- Test: `test/Test/Domain/AwtrixAddressTests.cs`, `test/Test/Services/AwtrixPublisherTests.cs`, `test/Test/Services/HttpPublisherTests.cs`, `test/Test/Services/AwtrixServicePublishTests.cs`

**Interfaces:**
- Consumes (Task 2): `AwtrixService.SafePublish`, `FakeHttpPublisher`, `StubHttpClientFactory`, `HttpPublisher(ILogger<HttpPublisher>, IHttpClientFactory)`.
- Produces:
  - `public virtual string AwtrixPublisher.BuildCustomAppUrl(string baseTopic, string appName)`
  - `public static bool AwtrixAddress.IsHttpTopic(string? topic)`
  - `public bool AwtrixAddress.IsHttp { get; }`

- [ ] **Step 1: Write the failing tests**

Append inside `public class AwtrixAddressTests` (`test/Test/Domain/AwtrixAddressTests.cs`):

```csharp
        [Theory]
        [InlineData("http://192.168.1.50/api", true)]
        [InlineData("https://192.168.1.50/api", true)]
        [InlineData("HTTP://192.168.1.50/api", true)]
        [InlineData("awtrix/clock1", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsHttpTopic_DetectsHttpSchemesCaseInsensitively(string? topic, bool expected)
        {
            Assert.Equal(expected, AwtrixAddress.IsHttpTopic(topic));
            Assert.Equal(expected, new AwtrixAddress { BaseTopic = topic! }.IsHttp);
        }
```

Append inside `public class AwtrixPublisherTests` (`test/Test/Services/AwtrixPublisherTests.cs`):

```csharp
        [Fact]
        public void BuildCustomAppUrl_Default_UsesMqttTopicForm()
        {
            var publisher = new RecordingPublisher();

            Assert.Equal("awtrix/clock1/custom/MyApp", publisher.BuildCustomAppUrl("awtrix/clock1", "MyApp"));
        }
```

Append inside `public class HttpPublisherTests` (`test/Test/Services/HttpPublisherTests.cs`):

```csharp
        [Theory]
        [InlineData("http://192.168.1.50/api", "TripTimerApp", "http://192.168.1.50/api/custom?name=TripTimerApp")]
        [InlineData("http://192.168.1.50/api/", "TripTimerApp", "http://192.168.1.50/api/custom?name=TripTimerApp")]
        [InlineData("http://192.168.1.50/api", "My App", "http://192.168.1.50/api/custom?name=My%20App")]
        public void BuildCustomAppUrl_UsesAwtrixHttpApiQueryForm(string baseTopic, string appName, string expected)
        {
            var publisher = CreatePublisher(StubHttpMessageHandler.Returning(HttpStatusCode.OK), out _);

            Assert.Equal(expected, publisher.BuildCustomAppUrl(baseTopic, appName));
        }
```

Append inside `public class AwtrixServicePublishTests` (`test/Test/Services/AwtrixServicePublishTests.cs`):

```csharp
        [Fact]
        public async Task HttpBaseTopic_AppUpdate_UsesCustomQueryNameUrl()
        {
            var (service, http, mqtt) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "http://192.168.1.50/api" };

            var result = await service.AppUpdate(address, "TripTimerApp", new AwtrixAppMessage().SetText("42"));

            Assert.True(result);
            Assert.Equal("http://192.168.1.50/api/custom?name=TripTimerApp", http.LastUrl);
            Assert.Contains("42", http.LastPayload);
            Assert.Equal(0, mqtt.PublishCallCount);
        }

        [Fact]
        public async Task HttpBaseTopicWithTrailingSlash_AppClear_UsesCustomQueryNameUrlWithEmptyPayload()
        {
            var (service, http, _) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "http://192.168.1.50/api/" };

            await service.AppClear(address, "TripTimerApp");

            Assert.Equal("http://192.168.1.50/api/custom?name=TripTimerApp", http.LastUrl);
            Assert.Equal(string.Empty, http.LastPayload);
        }

        [Fact]
        public async Task HttpBaseTopic_Settings_PathUnchanged()
        {
            var (service, http, _) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "http://192.168.1.50/api" };

            await service.Set(address, new AwtrixSettings().SetBrightness(8));

            Assert.Equal("http://192.168.1.50/api/settings", http.LastUrl);
        }

        [Fact]
        public async Task UppercaseHttpScheme_RoutesToHttpPublisher()
        {
            var (service, http, mqtt) = CreateService();
            var address = new AwtrixAddress { BaseTopic = "HTTP://192.168.1.50/api" };

            await service.Dismiss(address);

            Assert.Equal(1, http.PublishCallCount);
            Assert.Equal(0, mqtt.PublishCallCount);
        }
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Domain.AwtrixAddressTests|FullyQualifiedName~Test.Services.AwtrixPublisherTests|FullyQualifiedName~Test.Services.HttpPublisherTests|FullyQualifiedName~Test.Services.AwtrixServicePublishTests"`
Expected: build FAILS with `CS0117: 'AwtrixAddress' does not contain a definition for 'IsHttpTopic'` and `CS1061: ... 'BuildCustomAppUrl'`.

- [ ] **Step 3: Implement AwtrixAddress (replace the whole file)**

`src/api/Domain/AwtrixAddress.cs`:

```csharp
namespace AwtrixSharpWeb.Domain
{

    public class AwtrixAddress
    {
        /// <summary>
        /// eg: "awtrix/clock1" (MQTT) or "http://192.168.1.50/api" (HTTP)
        /// </summary>
        public string BaseTopic { get; set; }

        /// <summary>
        /// True when BaseTopic addresses the device's HTTP API. Get-only, so ignored by configuration binding.
        /// </summary>
        public bool IsHttp => IsHttpTopic(BaseTopic);

        public static bool IsHttpTopic(string? topic)
        {
            return topic != null
                && (topic.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || topic.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        public override string ToString() => BaseTopic;
    }
}
```

- [ ] **Step 4: Add BuildCustomAppUrl to the publishers**

In `src/api/Services/AwtrixPublisher.cs`, add this method directly after `ToJson(...)`:

```csharp
        /// <summary>
        /// Address of a custom app for this transport. Default is the MQTT topic form.
        /// </summary>
        public virtual string BuildCustomAppUrl(string baseTopic, string appName)
        {
            return $"{baseTopic}/custom/{appName}";
        }
```

In `src/api/Services/HttpPublisher.cs`, add this method directly after the constructor:

```csharp
        /// <summary>
        /// Awtrix 3 HTTP API: POST http://[ip]/api/custom?name=[app]
        /// </summary>
        public override string BuildCustomAppUrl(string baseTopic, string appName)
        {
            return $"{baseTopic.TrimEnd('/')}/custom?name={Uri.EscapeDataString(appName)}";
        }
```

- [ ] **Step 5: Use it in AwtrixService**

In `src/api/Services/AwtrixService.cs`, replace the `AppUpdate`, `AppClear` and `ResolvePublisher` methods with:

```csharp
        public Task<bool> AppUpdate(AwtrixAddress awtrixAddress, string appName, AwtrixAppMessage message)
        {
            var baseTopic = awtrixAddress.BaseTopic;
            return SafePublish(baseTopic, p => p.Publish(p.BuildCustomAppUrl(baseTopic, appName), message));
        }

        public Task<bool> AppClear(AwtrixAddress awtrixAddress, string appName)
        {
            var baseTopic = awtrixAddress.BaseTopic;
            return SafePublish(baseTopic, p => p.Publish(p.BuildCustomAppUrl(baseTopic, appName), (AwtrixAppMessage?)null));
        }
```

```csharp
        private AwtrixPublisher ResolvePublisher(string baseTopic)
        {
            return AwtrixAddress.IsHttpTopic(baseTopic) ? _httpPublisher : _mqttPublisher;
        }
```

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Domain.AwtrixAddressTests|FullyQualifiedName~Test.Services.AwtrixPublisherTests|FullyQualifiedName~Test.Services.HttpPublisherTests|FullyQualifiedName~Test.Services.AwtrixServicePublishTests"`
Expected: PASS, 0 failed. The existing `AppUpdate_PublishesToCustomAppTopic` (MQTT `awtrix/clock1/custom/MyApp`) and `HttpBaseTopic_RoutesToHttpPublisher` (`http://192.168.1.50/notify/dismiss`) still pass.

- [ ] **Step 7: Run the full suite**

Run: `dotnet test`
Expected: 0 failed.

- [ ] **Step 8: Commit**

```bash
git add src/api/Domain/AwtrixAddress.cs src/api/Services/AwtrixPublisher.cs src/api/Services/HttpPublisher.cs src/api/Services/AwtrixService.cs test/Test/Domain/AwtrixAddressTests.cs test/Test/Services/AwtrixPublisherTests.cs test/Test/Services/HttpPublisherTests.cs test/Test/Services/AwtrixServicePublishTests.cs
git commit -m "fix(http): use Awtrix HTTP API custom?name= form for custom apps

CR-12: publishers build custom-app addresses; HTTP maps to
{base}/custom?name={app}, MQTT topics unchanged. Transport detection moves
to AwtrixAddress.IsHttpTopic (case-insensitive scheme).

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 4: Async-safe tick handlers in apps (CR-01 app side, DiurnalApp clock seam)

**Files:**
- Modify: `src/api/Apps/AwtrixApp.cs` (add `FireAndLog`)
- Modify: `src/api/Domain/AwtrixSettings.cs` (`ToString`)
- Modify (full rewrite): `src/api/Apps/Diurnal/DiurnalApp.cs`
- Modify: `src/api/Apps/TripTimer/TripTimerApp.cs` (`ClockTickSecond`)
- Modify: `src/api/Apps/MqttRender/MqttClockRenderApp.cs` (`ClockTick`)
- Modify: `src/api/HostedServices/Conductor.cs` (DiurnalApp constructor call)
- Create: `test/Test/Apps/Diurnal/DiurnalAppTests.cs`
- Create: `test/Test/Apps/TripTimer/TripTimerAppTickTests.cs`
- Modify: `test/Test/Domain/AwtrixSettingsTests.cs`. It replaces `ToString_WhenEmpty_ThrowsInvalidOperationException`, because the documented crash is the bug being fixed.

**Interfaces:**
- Consumes (Task 1): `ClockTickEventArgs.Time` is local, `Kind=Local`, truncated to second.
- Produces:
  - `protected Task AwtrixApp<TConfig>.FireAndLog(Func<Task> work, string operation)` (never faults)
  - `public DiurnalApp(ILogger logger, IClock clock, ITimerService timerService, AppConfig config, AwtrixAddress awtrixAddress, IAwtrixService awtrixService)`
  - `AwtrixSettings.ToString()` returns `""` when empty

- [ ] **Step 1: Write the failing DiurnalApp tests**

`test/Test/Apps/Diurnal/DiurnalAppTests.cs`:

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
    public class DiurnalAppTests
    {
        private static readonly TimeSpan Aest = TimeSpan.FromHours(10);

        private readonly Mock<ITimerService> _timer = new();
        private readonly Mock<IAwtrixService> _awtrix = new();
        private readonly AwtrixAddress _address = new() { BaseTopic = "awtrix/clock1" };

        private DiurnalApp CreateApp(DateTimeOffset now, params (string Time, string Value)[] entries)
        {
            var config = AppConfig.Empty().WithName(AppNames.DiurnalApp);
            foreach (var (time, value) in entries)
            {
                config.Config[time] = value;
            }

            return new DiurnalApp(NullLogger.Instance, new MockClock(now), _timer.Object, config, _address, _awtrix.Object);
        }

        private static DateTimeOffset At(int hour, int minute) => new DateTimeOffset(2026, 9, 13, hour, minute, 0, Aest);

        private void RaiseMinute(int hour, int minute, int second = 0)
        {
            _timer.Raise(t => t.MinuteChanged += null,
                new ClockTickEventArgs(new DateTime(2026, 9, 13, hour, minute, second, DateTimeKind.Local)));
        }

        private static bool HasBrightness(AwtrixSettings s, string value) => s.ContainsKey("BRI") && s["BRI"] == value;

        [Fact]
        public void MinuteTick_MatchingEntry_AppliesSettings()
        {
            var app = CreateApp(At(0, 30), ("0600", "Brightness=8"));
            app.Init();

            RaiseMinute(6, 0);

            _awtrix.Verify(a => a.Set(_address, It.Is<AwtrixSettings>(s => HasBrightness(s, "8"))), Times.Once);
        }

        [Fact]
        public void MinuteTick_WithNonZeroSeconds_StillMatchesEntry()
        {
            var app = CreateApp(At(0, 30), ("0600", "Brightness=8"));
            app.Init();

            RaiseMinute(6, 0, second: 7);

            _awtrix.Verify(a => a.Set(_address, It.IsAny<AwtrixSettings>()), Times.Once);
        }

        [Fact]
        public void MinuteTick_WhenSetFaults_DoesNotPropagate()
        {
            _awtrix.Setup(a => a.Set(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixSettings>()))
                .ThrowsAsync(new HttpRequestException("device offline"));
            var app = CreateApp(At(0, 30), ("0600", "Brightness=8"));
            app.Init();

            var exception = Record.Exception(() => RaiseMinute(6, 0));

            Assert.Null(exception);
        }

        [Fact]
        public void MinuteTick_OutOfRangeBrightness_DoesNotThrowAndDoesNotPublish()
        {
            var app = CreateApp(At(0, 30), ("2100", "Brightness=300"));
            app.Init();

            var exception = Record.Exception(() => RaiseMinute(21, 0));

            Assert.Null(exception);
            _awtrix.Verify(a => a.Set(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixSettings>()), Times.Never);
        }

        [Fact]
        public void MinuteTick_UnknownKeyOnly_SkipsSetWithoutThrowing()
        {
            var app = CreateApp(At(0, 30), ("0600", "Brightnes=8"));
            app.Init();

            var exception = Record.Exception(() => RaiseMinute(6, 0));

            Assert.Null(exception);
            _awtrix.Verify(a => a.Set(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixSettings>()), Times.Never);
        }

        [Fact]
        public void Init_AfterEntryWithUnknownKey_StartupReplayDoesNotThrow()
        {
            var app = CreateApp(At(7, 0), ("0600", "Brightnes=8"));

            var exception = Record.Exception(() => app.Init());

            Assert.Null(exception);
            _awtrix.Verify(a => a.Set(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixSettings>()), Times.Never);
        }

        [Fact]
        public void Init_ReplaysEarlierEntriesUsingInjectedClock()
        {
            var app = CreateApp(At(7, 0), ("0600", "Brightness=8"), ("2100", "Brightness=1"));

            app.Init();

            _awtrix.Verify(a => a.Set(_address, It.Is<AwtrixSettings>(s => HasBrightness(s, "8"))), Times.Once);
            _awtrix.Verify(a => a.Set(_address, It.Is<AwtrixSettings>(s => HasBrightness(s, "1"))), Times.Never);
        }
    }
}
```

- [ ] **Step 2: Write the failing TripTimerApp tick tests**

`test/Test/Apps/TripTimer/TripTimerAppTickTests.cs`:

```csharp
using System.Reflection;
using AwtrixSharpWeb.Apps.TripTimer;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Test.Domain;

namespace Test.Apps.TripTimer
{
    /// <summary>
    /// ClockTickSecond runs on the timer loop; it must never let an exception escape.
    /// Invoked via reflection (private handler), matching TripTimerAppBoundaryTests' style.
    /// </summary>
    public class TripTimerAppTickTests
    {
        private static readonly DateTimeOffset Departure = DateTimeOffset.Parse("2025-08-19T06:41:00+10:00");

        private static (TripTimerApp app, Mock<IAwtrixService> awtrix) Create(DateTimeOffset now, CancellationTokenSource cts)
        {
            var awtrix = new Mock<IAwtrixService>();
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
                new Mock<ITripPlannerService>().Object);

            app.NextDepartures.Add(TripSummaryTests.Create(Departure));

            var ctsField = typeof(TripTimerApp).GetField("_cts", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            ctsField!.SetValue(app, cts);

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
            var (app, awtrix) = Create(now, new CancellationTokenSource());
            awtrix.Setup(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()))
                .ThrowsAsync(new HttpRequestException("device offline"));

            var exception = Record.Exception(() => InvokeClockTickSecond(app, now.DateTime));

            Assert.Null(exception);
            awtrix.Verify(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()), Times.Once);
        }

        [Fact]
        public void SecondTick_NoFutureDeparturesWithDisposedCts_DoesNotPropagate()
        {
            var now = Departure.AddMinutes(5);
            var disposed = new CancellationTokenSource();
            disposed.Dispose();
            var (app, _) = Create(now, disposed);

            var exception = Record.Exception(() => InvokeClockTickSecond(app, now.DateTime));

            Assert.Null(exception);
        }
    }
}
```

- [ ] **Step 3: Update the AwtrixSettings empty-ToString test**

In `test/Test/Domain/AwtrixSettingsTests.cs`, replace the whole `ToString_WhenEmpty_ThrowsInvalidOperationException` method with:

```csharp
        [Fact]
        public void ToString_WhenEmpty_ReturnsEmptyString()
        {
            // Previously threw InvalidOperationException (Aggregate on empty) inside a tick handler (CR-01).
            var settings = new AwtrixSettings();

            Assert.Equal(string.Empty, settings.ToString());
        }
```

- [ ] **Step 4: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps.Diurnal.DiurnalAppTests|FullyQualifiedName~Test.Apps.TripTimer.TripTimerAppTickTests|FullyQualifiedName~Test.Domain.AwtrixSettingsTests"`
Expected: build FAILS with `CS1503` / `CS1729` on the `DiurnalApp` constructor (no `IClock` parameter). After Step 7 alone, the TripTimer and AwtrixSettings tests would fail at runtime with `TargetInvocationException` / `InvalidOperationException`.

- [ ] **Step 5: Add FireAndLog to AwtrixApp**

In `src/api/Apps/AwtrixApp.cs`, add these methods directly after `public virtual void ExecuteNow() { }`:

```csharp
        /// <summary>
        /// Run async work from a synchronous event handler (e.g. a clock tick) without blocking
        /// the caller and without ever letting an exception escape. The returned task never faults;
        /// callers normally discard it.
        /// </summary>
        protected Task FireAndLog(Func<Task> work, string operation)
        {
            return RunAndLogAsync(work, operation);
        }

        private async Task RunAndLogAsync(Func<Task> work, string operation)
        {
            try
            {
                await work();
            }
            catch (OperationCanceledException)
            {
                Logger.LogDebug("{Operation} cancelled for {AppType} on {AwtrixAddress}", operation, Config.Type, AwtrixAddress);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{Operation} failed for {AppType} on {AwtrixAddress}", operation, Config.Type, AwtrixAddress);
            }
        }
```

- [ ] **Step 6: Make AwtrixSettings.ToString safe**

In `src/api/Domain/AwtrixSettings.cs`, replace the `ToString` override with:

```csharp
        public override string ToString()
        {
            return string.Join(";", this.Select(kv => $"{kv.Key}={kv.Value}"));
        }
```

- [ ] **Step 7: Rewrite DiurnalApp (replace the whole file)**

`src/api/Apps/Diurnal/DiurnalApp.cs`:

```csharp
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using System.Globalization;

namespace AwtrixSharpWeb.Apps.Diurnal
{
    /// <summary>
    /// Change brightness and colour based on time of day
    /// </summary>
    public class DiurnalApp : AwtrixApp<AppConfig>
    {
        private readonly ITimerService _timerService;
        private readonly IClock _clock;

        private readonly Dictionary<TimeSpan, List<Action<AwtrixSettings>>> _timeActionMap;

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
            _timeActionMap = new Dictionary<TimeSpan, List<Action<AwtrixSettings>>>();
        }

        protected override void Initialize()
        {
            _timerService.MinuteChanged += ClockTickMinute;

            // Check if Config dictionary is populated
            if (Config.Config == null || Config.Config.Count == 0)
            {
                Logger.LogWarning("DiurnalApp Config is empty. Make sure it's properly configured in appsettings.json");
                return;
            }

            Logger.LogDebug("DiurnalApp initializing with {Count} time entries", Config.Config.Count);

            foreach (var time in Config.Config.Keys)
            {
                try
                {
                    var timeSpan = TimeSpan.ParseExact(time, "hhmm", CultureInfo.InvariantCulture);
                    var value = Config.Config[time];

                    var valueParts = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var keyPair in valueParts)
                    {
                        var keyPairParts = keyPair.Split('=', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (keyPairParts.Length == 2)
                        {
                            var settingKey = keyPairParts[0].ToLower();
                            var settingValue = keyPairParts[1];
                            if (!_timeActionMap.ContainsKey(timeSpan))
                            {
                                _timeActionMap[timeSpan] = new List<Action<AwtrixSettings>>();
                            }

                            var actions = _timeActionMap[timeSpan];

                            switch (settingKey)
                            {
                                case "brightness":
                                    actions.Add(a => a.SetBrightness(byte.Parse(settingValue)));
                                    break;
                                case "globaltextcolor":
                                    actions.Add(a => a.SetGlobalTextColor(settingValue));
                                    break;
                                default:
                                    Logger.LogWarning("Unknown setting key '{SettingKey}' in config for hour {Hour}", settingKey, time);
                                    break;
                            }
                        }
                    }

                    Logger.LogDebug("Config Key: {Key} = {Value}", time, Config.Config[time]);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error processing time entry {Time}", time);
                }
            }

            // Replay today's earlier entries so the clock reflects the current period at startup
            var now = _clock.Now;
            var currentTime = now.TimeOfDay;
            var previousSettings = _timeActionMap
                                .Keys
                                .Order()
                                .Where(t => t < currentTime)
                                .ToList();

            Logger.LogInformation("Replaying {Count} previous time entry settings", previousSettings.Count);

            foreach (var setting in previousSettings)
            {
                var triggerTime = DateTime.SpecifyKind(now.DateTime.Date.Add(setting), DateTimeKind.Local);
                ClockTickMinute(this, new ClockTickEventArgs(triggerTime));
            }
        }

        private void ClockTickMinute(object? sender, ClockTickEventArgs e)
        {
            // e.Time is local wall-clock time (TimerService contract) - no ToLocalTime() conversion
            var time = e.Time.TimeOfDay;
            var minute = new TimeSpan(time.Hours, time.Minutes, 0);
            _ = FireAndLog(() => ApplySettingsAsync(minute), nameof(ClockTickMinute));
        }

        private async Task ApplySettingsAsync(TimeSpan minute)
        {
            if (!_timeActionMap.TryGetValue(minute, out var actions))
            {
                return;
            }

            var awtrixSetting = new AwtrixSettings();
            foreach (var action in actions)
            {
                action(awtrixSetting);
            }

            if (awtrixSetting.Count == 0)
            {
                Logger.LogWarning("{BaseTopic} @ {Time}: no valid settings to apply; skipping", AwtrixAddress.BaseTopic, minute);
                return;
            }

            Logger.LogInformation("{BaseTopic} @ {Time}: Applying global setting {AwtrixSetting}", AwtrixAddress.BaseTopic, minute, awtrixSetting);
            await Set(awtrixSetting);
        }
    }
}
```

- [ ] **Step 8: Make TripTimerApp and MqttClockRenderApp tick handlers async-safe**

In `src/api/Apps/TripTimer/TripTimerApp.cs`, replace

```csharp
        private void ClockTickSecond(object? sender, ClockTickEventArgs e)
        {
            var message = BuildMessage(e);
            _ = AppUpdate(message).Result;
        }
```

with

```csharp
        private void ClockTickSecond(object? sender, ClockTickEventArgs e)
        {
            // BuildMessage runs inside FireAndLog so its _cts.Cancel() cannot escape onto the timer loop
            _ = FireAndLog(async () =>
            {
                var message = BuildMessage(e);
                await AppUpdate(message);
            }, nameof(ClockTickSecond));
        }
```

In `src/api/Apps/MqttRender/MqttClockRenderApp.cs`, replace

```csharp
        private void ClockTick(object? sender, ClockTickEventArgs e)
        {
            _currentTime= e.Time;
            UpdateDisplay();
        }
```

with

```csharp
        private void ClockTick(object? sender, ClockTickEventArgs e)
        {
            _currentTime = e.Time;
            _ = FireAndLog(() => UpdateDisplay(), nameof(ClockTick));
        }
```

- [ ] **Step 9: Pass the clock to DiurnalApp in Conductor**

In `src/api/HostedServices/Conductor.cs`, replace

```csharp
                        app = new DiurnalApp(appLogger, _timerService, appConfig, device, awtrixService);
```

with

```csharp
                        app = new DiurnalApp(appLogger, clock, _timerService, appConfig, device, awtrixService);
```

- [ ] **Step 10: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.Apps|FullyQualifiedName~Test.Domain.AwtrixSettingsTests|FullyQualifiedName~Test.HostedServices.ConductorTests"`
Expected: PASS, 0 failed. This includes the existing `TripTimerAppBoundaryTests` and `ConductorTests.AppFactory_DiurnalApp_CreatesDiurnalApp`.

- [ ] **Step 11: Run the full suite**

Run: `dotnet test`
Expected: 0 failed.

- [ ] **Step 12: Commit**

```bash
git add src/api/Apps/AwtrixApp.cs src/api/Domain/AwtrixSettings.cs src/api/Apps/Diurnal/DiurnalApp.cs src/api/Apps/TripTimer/TripTimerApp.cs src/api/Apps/MqttRender/MqttClockRenderApp.cs src/api/HostedServices/Conductor.cs test/Test/Apps/Diurnal/DiurnalAppTests.cs test/Test/Apps/TripTimer/TripTimerAppTickTests.cs test/Test/Domain/AwtrixSettingsTests.cs
git commit -m "fix(apps): async-safe tick handlers via FireAndLog

CR-01: Diurnal, TripTimer and MqttClockRender tick handlers no longer block
on .Result or discard faulting tasks; work runs through AwtrixApp.FireAndLog
which logs and never faults. AwtrixSettings.ToString is safe when empty;
Diurnal skips empty settings and takes IClock instead of DateTime.Now.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

### Task 5: DI seams, testable Conductor, HTTP ButtonApp skip, safe StopAsync (CR-35, CR-12 buttons, CR-02 shutdown)

**Files:**
- Create: `src/api/Interfaces/ISlackConnector.cs`
- Create: `test/Test/CompositionRootTests.cs`
- Modify: `src/api/HostedServices/SlackConnector.cs` (class declaration line only)
- Modify: `src/api/Apps/SlackStatus/SlackStatusApp.cs` (usings, field, constructor)
- Modify: `src/api/Services/IAwtrixService.cs` (add `PlayRtttl`)
- Modify: `src/api/Controllers/DiagnosticsController.cs` (`AwtrixService` → `IAwtrixService`)
- Modify (full rewrite): `src/api/Domain/Clock.cs`, `src/api/HostedServices/Conductor.cs`, `src/api/Program.cs`, `test/Test/HostedServices/ConductorTestHelper.cs`
- Modify: `test/Test/HostedServices/ConductorTests.cs` (add tests)

**Interfaces:**
- Consumes:
  - Task 1: `TimerService(ILogger<TimerService>, TimeProvider?)`
  - Task 2: `HttpPublisher.HttpClientName`, `HttpPublisher.DefaultTimeout`, `StubHttpClientFactory`, `StubHttpMessageHandler`, `FakeMqttPublisher`, `IMqttConnector.PublishAsync`
  - Task 3: `DeviceConfig.IsHttp`
  - Task 4: `DiurnalApp(ILogger, IClock, ITimerService, AppConfig, AwtrixAddress, IAwtrixService)`
- Produces:
  - `public interface ISlackConnector { event EventHandler<SlackUserStatusChangedEventArgs>? UserStatusChanged; }`
  - `Conductor(ILogger<Conductor>, IHostEnvironment, IOptions<AwtrixConfig>, ITimerService, ITripPlannerService, IAwtrixService, ISlackConnector, IMqttConnector, IClock, ILoggerFactory)`
  - `SlackStatusApp(ILogger, SlackStatusAppConfig, AwtrixAddress, IAwtrixService, ISlackConnector)`
  - `IAwtrixService.PlayRtttl(AwtrixAddress, string) : Task<bool>`
  - `public Clock(TimeProvider? timeProvider = null)`
  - `public static void Program.AddAwtrixServices(IServiceCollection services, IConfiguration configuration)`
  - `ConductorTestHelper.Create(AwtrixConfig? config = null, IHostEnvironment? hostEnvironment = null, IAwtrixService? awtrixService = null, IMqttConnector? mqttConnector = null, IClock? clock = null)`

- [ ] **Step 1: Write the failing Conductor tests**

In `test/Test/HostedServices/ConductorTests.cs`, add these usings to the using block:

```csharp
using System.Net;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Test.Apps;
using Test.Services;
```

Then append these methods inside `public class ConductorTests`, before `private static void SetAppsList(...)`:

```csharp
        private static readonly DateTimeOffset HalfPastMidnight = new DateTimeOffset(2026, 9, 13, 0, 30, 0, TimeSpan.FromHours(10));

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

            Assert.Single(conductor.FindApps(AppNames.ButtonApp));
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

        [Fact]
        public async Task StopAsync_WhenOneAppDisposeThrows_StillDisposesOthers()
        {
            var conductor = ConductorTestHelper.Create();
            var throwing = new Mock<IAwtrixApp>();
            throwing.Setup(a => a.Dispose()).Throws(new AggregateException(new HttpRequestException("offline")));
            var healthy = new Mock<IAwtrixApp>();
            SetAppsList(conductor, new List<IAwtrixApp> { throwing.Object, healthy.Object });

            var exception = await Record.ExceptionAsync(() => conductor.StopAsync(CancellationToken.None));

            Assert.Null(exception);
            healthy.Verify(a => a.Dispose(), Times.Once);
        }
```

- [ ] **Step 2: Write the failing composition-root tests**

`test/Test/CompositionRootTests.cs`:

```csharp
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Test
{
    /// <summary>
    /// Validates the application's DI graph (minus MVC/Swagger) so interface seams can't
    /// silently break at runtime.
    /// </summary>
    public class CompositionRootTests
    {
        private static ServiceProvider BuildProvider()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOptions();
            services.AddSingleton<IHostEnvironment>(Mock.Of<IHostEnvironment>(e => e.EnvironmentName == "Production"));

            AwtrixSharpWeb.Program.AddAwtrixServices(services, new ConfigurationBuilder().Build());

            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        }

        [Fact]
        public void Graph_ValidatesOnBuild_AndConductorResolves()
        {
            using var provider = BuildProvider();

            Assert.NotNull(provider.GetRequiredService<Conductor>());
        }

        [Fact]
        public void InterfaceSeams_ResolveToTheSameSingletons()
        {
            using var provider = BuildProvider();

            Assert.Same(provider.GetRequiredService<TimerService>(), provider.GetRequiredService<ITimerService>());
            Assert.Same(provider.GetRequiredService<MqttConnector>(), provider.GetRequiredService<IMqttConnector>());
            Assert.Same(provider.GetRequiredService<SlackConnector>(), provider.GetRequiredService<ISlackConnector>());
            Assert.Same(provider.GetRequiredService<IAwtrixService>(), provider.GetRequiredService<IAwtrixService>());
            Assert.IsType<AwtrixService>(provider.GetRequiredService<IAwtrixService>());
            Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
            Assert.IsType<Clock>(provider.GetRequiredService<IClock>());
            Assert.NotNull(provider.GetRequiredService<ITripPlannerService>());
        }

        [Fact]
        public void HttpPublisherNamedClient_HasFiveSecondTimeout()
        {
            using var provider = BuildProvider();

            var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpPublisher.HttpClientName);

            Assert.Equal(TimeSpan.FromSeconds(5), client.Timeout);
        }

        [Fact]
        public void RegistersFourHostedServicesInStartupOrder()
        {
            using var provider = BuildProvider();

            var hosted = provider.GetServices<IHostedService>().ToList();

            Assert.Collection(hosted,
                h => Assert.IsType<MqttConnector>(h),
                h => Assert.IsType<SlackConnector>(h),
                h => Assert.IsType<Conductor>(h),
                h => Assert.IsType<TimerService>(h));
        }
    }
}
```

- [ ] **Step 3: Rewrite ConductorTestHelper (replace the whole file)**

`test/Test/HostedServices/ConductorTestHelper.cs`:

```csharp
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
            IClock? clock = null)
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
                new Mock<ITimerService>().Object,
                new Mock<ITripPlannerService>().Object,
                awtrixService ?? new Mock<IAwtrixService>().Object,
                new Mock<ISlackConnector>().Object,
                mqttConnector ?? new Mock<IMqttConnector>().Object,
                clock ?? new Clock(),
                NullLoggerFactory.Instance);
        }
    }
}
```

- [ ] **Step 4: Run the tests and confirm they fail**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.HostedServices.ConductorTests|FullyQualifiedName~Test.CompositionRootTests"`
Expected: build FAILS with `CS0246: The type or namespace name 'ISlackConnector' could not be found`, `CS1503` on the `Conductor` constructor argument types, and `CS0117: 'Program' does not contain a definition for 'AddAwtrixServices'`.

- [ ] **Step 5: Add ISlackConnector and adopt it**

`src/api/Interfaces/ISlackConnector.cs`:

```csharp
using AwtrixSharpWeb.HostedServices;

namespace AwtrixSharpWeb.Interfaces
{
    public interface ISlackConnector
    {
        event EventHandler<SlackUserStatusChangedEventArgs>? UserStatusChanged;
    }
}
```

In `src/api/HostedServices/SlackConnector.cs`, add `using AwtrixSharpWeb.Interfaces;` to the using block. Then replace

```csharp
    public class SlackConnector : IHostedService, IEventHandler<UserChange>
```

with

```csharp
    public class SlackConnector : IHostedService, IEventHandler<UserChange>, ISlackConnector
```

In `src/api/Apps/SlackStatus/SlackStatusApp.cs`, add `using AwtrixSharpWeb.Interfaces;` to the using block. Then replace

```csharp
        SlackConnector _slackConnector;
        string _trackingUserId;

        public SlackStatusApp(
            ILogger logger
            , SlackStatusAppConfig config
            , AwtrixAddress awtrixAddress
            , AwtrixService awtrixService
            , SlackConnector slackConnector) : base(logger, config, awtrixAddress, awtrixService)
```

with

```csharp
        ISlackConnector _slackConnector;
        string _trackingUserId;

        public SlackStatusApp(
            ILogger logger
            , SlackStatusAppConfig config
            , AwtrixAddress awtrixAddress
            , IAwtrixService awtrixService
            , ISlackConnector slackConnector) : base(logger, config, awtrixAddress, awtrixService)
```

- [ ] **Step 6: Add PlayRtttl to IAwtrixService and switch DiagnosticsController to the interface**

`src/api/Services/IAwtrixService.cs` (replace the whole file):

```csharp
using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Services
{
    /// <summary>
    /// Device operations. Implementations never throw; false means "not delivered".
    /// </summary>
    public interface IAwtrixService
    {
        Task<bool> AppClear(AwtrixAddress awtrixAddress, string appName);
        Task<bool> AppUpdate(AwtrixAddress awtrixAddress, string appName, AwtrixAppMessage message);
        Task<bool> Dismiss(AwtrixAddress awtrixAddress);
        Task<bool> Notify(AwtrixAddress awtrixAddress, AwtrixAppMessage message);
        Task<bool> Set(AwtrixAddress awtrixAddress, AwtrixSettings settings);
        Task<bool> PlayRtttl(AwtrixAddress awtrixAddress, string rtttl);
    }
}
```

In `src/api/Controllers/DiagnosticsController.cs`, replace `private readonly AwtrixService _awtrixService;` with `private readonly IAwtrixService _awtrixService;`, and replace the constructor parameter `, AwtrixService awtrixService` with `, IAwtrixService awtrixService`.

- [ ] **Step 7: Back Clock with TimeProvider (replace the whole file)**

`src/api/Domain/Clock.cs`:

```csharp
using AwtrixSharpWeb.Interfaces;

namespace AwtrixSharpWeb.Domain
{
    public class Clock : IClock
    {
        private readonly TimeProvider _timeProvider;

        public Clock(TimeProvider? timeProvider = null)
        {
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public DateTimeOffset Now => _timeProvider.GetLocalNow();
    }
}
```

- [ ] **Step 8: Rewrite Conductor over interfaces (replace the whole file)**

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
    }

    /// <summary>
    /// Orchestrates the various Awtrix apps based on configuration
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
        AwtrixConfig _awtrixConfig;

        List<IAwtrixApp> _apps;

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

            _apps = new List<IAwtrixApp>();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var device in _awtrixConfig.Devices)
            {
                ButtonApp? buttonApp = null;

                if (device.IsHttp)
                {
                    _logger.LogInformation(
                        "Device {Device} uses the HTTP transport; hardware buttons are not supported, ButtonApp not created",
                        device.BaseTopic);
                }
                else
                {
                    buttonApp = (ButtonApp)AppFactory(device, AppConfig.Empty().WithName(AppNames.ButtonApp));
                    _apps.Add(buttonApp);

                    buttonApp.Click += (s, e) =>
                    {
                        _logger.LogInformation("{Button} button clicked on {Device}", e.Button, device.BaseTopic);
                    };

                    buttonApp.DoubleClick += (s, e) =>
                    {
                        _logger.LogInformation("{Button} button double-clicked on {Device}", e.Button, device.BaseTopic);
                    };
                }

                foreach (var appConfig in device.Apps)
                {
                    // Log the app configuration to debug configuration binding issues
                    LogAppConfigDetails(appConfig);

                    var app = AppFactory(device, appConfig);

                    // Hacky binding for now
                    if (app is TripTimerApp tripTimerApp && buttonApp != null)
                    {
                        buttonApp.DoubleClick += (s, e) =>
                        {
                            if (e.Button == Button.Right)
                            {
                                tripTimerApp.ExecuteNow();
                            }
                        };
                    }

                    _apps.Add(app);
                }

                foreach (var app in _apps)
                {
                    app.Init();
                }
            }

            return Task.CompletedTask;
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

        private IAwtrixApp AppFactory(DeviceConfig device, AppConfig appConfig)
        {
            IAwtrixApp app;

            _logger.LogInformation(
                "Creating {AppName} for {device}"
                , appConfig.Name
                , device.BaseTopic);

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
                        var appLogger = _loggerFactory.CreateLogger<MqttRenderApp>();
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
                    throw new NotImplementedException(appConfig.Type);
            }

            return app;
        }

        public void ExecuteNow(string baseTopic, string appName)
        {
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
                app.Init();
                app.ExecuteNow();
                _logger.LogInformation("Successfully executed app '{AppName}' on device '{BaseTopic}'", appName, baseTopic);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing app '{AppName}' on device '{BaseTopic}'", appName, baseTopic);
            }
        }

        public List<IAwtrixApp> FindApps(string appName)
        {
            var app = _apps.FindAll(a => a.GetConfig().Type == appName);
            return app;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Conductor stopping");

            foreach (var app in _apps)
            {
                try
                {
                    app.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing {AppType} on {Device}", app.GetConfig()?.Type, app.AwtrixAddress);
                }
            }

            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 9: Extract the composition root in Program.cs (replace the whole file)**

`src/api/Program.cs`:

```csharp
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using System.Reflection;
using TransportOpenData;
using TransportOpenData.TripPlanner;

namespace AwtrixSharpWeb
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var configuration = builder.Configuration;

            var services = builder.Services;

            SetupConfiguration(configuration, services);

            ConfigureLogging(builder);

            services.AddControllers();

            AddAwtrixServices(services, configuration);

            RegisterSwagger(services);

            var app = builder.Build();

            LogStartup(app);

            // if (app.Environment.IsDevelopment()) always show swagger
            {
                app.UseDeveloperExceptionPage();

                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.MapControllers();

            app.Run();
        }

        /// <summary>
        /// Application services (everything except MVC and Swagger). Public so the DI graph can be validated in tests.
        /// </summary>
        public static void AddAwtrixServices(IServiceCollection services, IConfiguration configuration)
        {
            // Configure Trip Planner settings
            services.Configure<TransportOpenDataConfig>(config =>
            {
                config.ApiKey = Environment.GetEnvironmentVariable("TRANSPORTOPENDATA__APIKEY") ?? "";
                config.BaseUrl = configuration.GetSection("TransportOpenData:BaseUrl").Value ?? "https://api.transport.nsw.gov.au/v1/tp";
            });

            // Time
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IClock, Clock>();

            // Trip planner
            services.AddTransient<TripPlannerService>();
            services.AddTransient<ITripPlannerService>(sp => sp.GetRequiredService<TripPlannerService>());

            services.AddHttpClient<StopfinderClient>((serviceProvider, client) =>
            {
                var config = serviceProvider.GetRequiredService<IOptions<TransportOpenDataConfig>>();

                // Set the authorization header
                client.DefaultRequestHeaders.Add("Authorization", $"apikey {config.Value.ApiKey}");
            });

            services.AddHttpClient<TripClient>((serviceProvider, client) =>
            {
                var config = serviceProvider.GetRequiredService<IOptions<TransportOpenDataConfig>>();

                // Set the authorization header
                client.DefaultRequestHeaders.Add("Authorization", $"apikey {config.Value.ApiKey}");
            });

            // Connectors
            services.AddSingleton<MqttConnector>();
            services.AddSingleton<IMqttConnector>(sp => sp.GetRequiredService<MqttConnector>());
            services.AddSingleton<SlackConnector>();
            services.AddSingleton<ISlackConnector>(sp => sp.GetRequiredService<SlackConnector>());

            // Publishing
            services.AddHttpClient(HttpPublisher.HttpClientName, client => client.Timeout = HttpPublisher.DefaultTimeout);
            services.AddSingleton<HttpPublisher>();
            services.AddSingleton<MqttPublisher>();
            services.AddSingleton<IAwtrixService, AwtrixService>();

            // Orchestration
            services.AddSingleton<TimerService>();
            services.AddSingleton<ITimerService>(sp => sp.GetRequiredService<TimerService>());
            services.AddSingleton<Conductor>();

            // Hosted services start in this order and stop in reverse
            services.AddHostedService(sp => sp.GetRequiredService<MqttConnector>());
            services.AddHostedService(sp => sp.GetRequiredService<SlackConnector>());
            services.AddHostedService(sp => sp.GetRequiredService<Conductor>());
            services.AddHostedService(sp => sp.GetRequiredService<TimerService>());
        }

        private static void SetupConfiguration(ConfigurationManager configuration, IServiceCollection services)
        {
            configuration
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables("AWTRIXSHARP_");

            services.Configure<MqttSettings>(configuration.GetSection("Mqtt"));
            services.Configure<AwtrixConfig>(configuration.GetSection("Awtrix"));
        }

        private static void ConfigureLogging(WebApplicationBuilder builder)
        {
            var logging = builder.Logging;
            logging.ClearProviders();
            logging.AddSimpleConsole(options =>
            {
                options.TimestampFormat = "HH:mm:ss ";
                options.SingleLine = true;
            });
            logging.AddDebug();
        }

        private static void RegisterSwagger(IServiceCollection services)
        {
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Awtrix API", Version = "v1" });

                // Enable annotations for Swagger
                c.EnableAnnotations();
            });
        }

        private static void LogStartup(WebApplication app)
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            logger.LogInformation("Starting AwtrixSharp v{Version}, {Commit}", version, GetGitCommitShort());
            logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);
        }

        public static string? GetGitCommitShort() =>
            Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "GitCommitShort")?.Value;
    }
}
```

Notes on this rewrite:
- It removes the unused usings `AwtrixSharpWeb.Apps`, `AwtrixSharpWeb.Apps.Configs`, `System.Text.Json`, `System.Text.Json.Serialization`, `Microsoft.Extensions.Configuration` and `Microsoft.Extensions.Logging`. The last two are implicit usings in the Web SDK.
- The concrete `AddTransient<AwtrixService>()` registration is intentionally replaced by the `IAwtrixService` singleton. `DiagnosticsController` now depends on the interface (Step 6).
- If the compiler reports a missing namespace, re-add only that `using`.

- [ ] **Step 10: Run the tests and confirm they pass**

Run: `dotnet test test/Test/Test.csproj --filter "FullyQualifiedName~Test.HostedServices.ConductorTests|FullyQualifiedName~Test.CompositionRootTests|FullyQualifiedName~Test.Controllers|FullyQualifiedName~Test.Apps.TripTimer.TripTimerControllerTests"`
Expected: PASS, 0 failed.

- [ ] **Step 11: Run the full suite**

Run: `dotnet test`
Expected: 0 failed.

- [ ] **Step 12: Smoke-start the host (DI check covering MVC/Swagger)**

Run: `dotnet run --project src/api` in the background. Wait for the log line `Starting AwtrixSharp`, then check that no `Unable to resolve service` / `InvalidOperationException` appears in the first 10 seconds, then stop the process. Broker or Slack connection warnings are expected when those services are not configured locally.
Expected: the host starts, and the log shows `Creating ... for ...` entries for the configured apps.

- [ ] **Step 13: Commit**

```bash
git add src/api/Interfaces/ISlackConnector.cs src/api/HostedServices/SlackConnector.cs src/api/Apps/SlackStatus/SlackStatusApp.cs src/api/Services/IAwtrixService.cs src/api/Controllers/DiagnosticsController.cs src/api/Domain/Clock.cs src/api/HostedServices/Conductor.cs src/api/Program.cs test/Test/HostedServices/ConductorTestHelper.cs test/Test/HostedServices/ConductorTests.cs test/Test/CompositionRootTests.cs
git commit -m "refactor(di): interface seams for Conductor and apps; validated composition root

CR-35: Conductor depends on ITimerService, ITripPlannerService,
IAwtrixService, ISlackConnector, IMqttConnector and IClock; Clock is backed
by TimeProvider; registrations move to Program.AddAwtrixServices with a
ValidateOnBuild test. CR-12: no ButtonApp for HTTP devices. CR-02:
Conductor.StopAsync disposes each app in isolation.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01X4dSCGSE1jGmwbMREypUyt"
```

---

## Self-review (completed by planner)

**Spec coverage:**

| Spec item | Where it is implemented |
|---|---|
| D1 (TimeProvider / IClock) | T1 (TimerService), T4 (DiurnalApp IClock), T5 (Clock over TimeProvider, DI) |
| D2 (tick semantics) | T1 tests and implementation; T4 Diurnal drops `ToLocalTime` |
| D3 (loop and isolation) | T1 |
| D4 (FireAndLog) | T4 |
| D5 (publisher contract) | T2 |
| D6 (IHttpClientFactory, 5 s) | T2; timeout registration test in T5 |
| D7 (custom-app URL) | T3 |
| D8 (ButtonApp skip) | T5 |
| D9 (DI seams, safe StopAsync) | T5 |
| D10 (settings / Diurnal hardening) | T4 |

- CR-01 AC1-8 → T1 and T4.
- CR-02 AC1-8 → T2 and T5.
- CR-11 AC1-5 → T1.
- CR-12 AC1-5 → T3 and T5.
- CR-35 AC1-4 → T4 and T5.

**Type consistency check:**
- `HttpPublisher(ILogger<HttpPublisher>, IHttpClientFactory)` is used identically in T2 (fakes, helper, tests) and T5 (ConductorTests).
- `DiurnalApp(ILogger, IClock, ITimerService, AppConfig, AwtrixAddress, IAwtrixService)` is the same in T4 (tests, Conductor edit) and T5 (Conductor rewrite).
- `ConductorTestHelper.Create` named parameters `awtrixService`, `mqttConnector` and `clock` match the T5 tests.
- `StubHttpMessageHandler.Requests` / `.Returning` and `StubHttpClientFactory.RequestedNames` are defined in T2 and used in T2, T3 and T5.
- `FakeMqttPublisher.ThrowOnPublish` is defined in T2 and used in T2.

**Existing tests changed explicitly:**
- `TimerServiceEventTests`: rewritten in T1.
- `AwtrixSettingsTests.ToString_WhenEmpty_ThrowsInvalidOperationException`: replaced in T4.
- `FakePublishers.cs`: base constructor changed in T2.
- `ConductorTestHelper.cs`: changed in T2, rewritten in T5.
- All other existing tests compile unchanged:
  - `new AwtrixService(http, mqtt)` still compiles (optional logger).
  - `new MqttPublisher(MqttConnector, ...)` still compiles (MqttConnector implements `IMqttConnector`).
  - `new TimerService(logger)` still compiles (optional TimeProvider).
