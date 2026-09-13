using System.Collections.Concurrent;
using System.Threading.Channels;
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
    /// CR-25: an active trip timer re-queries departures, keeps the last good list when a query fails, backs off,
    /// re-queries before giving up, and stops (cancelling its in-flight request) when the window ends.
    /// The refresh wait is scripted, so each loop step is released explicitly rather than by timers.
    /// </summary>
    public class TripTimerAppRefreshTests
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
        private readonly ScriptedDelay _delays = new();
        private readonly ConcurrentQueue<CancellationToken> _plannerTokens = new();
        private Func<CancellationToken, Task<List<TripSummary>>> _plannerResult = _ => Task.FromResult(Departures(Departure));

        public TripTimerAppRefreshTests()
        {
            _awtrix.Setup(a => a.Notify(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixAppMessage>())).ReturnsAsync(true);
            _awtrix.Setup(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>())).ReturnsAsync(true);
            _awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>())).ReturnsAsync(true);
            _planner
                .Setup(p => p.GetNextDepartures(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .Returns<string, string, DateTimeOffset, CancellationToken>((_, _, _, token) =>
                {
                    _plannerTokens.Enqueue(token);
                    return _plannerResult(token);
                });
        }

        private static List<TripSummary> Departures(params DateTimeOffset[] departs) =>
            departs.Select(depart => TripSummaryTests.Create(depart)).ToList();

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

            var app = new TripTimerApp(_logger.Object, new Clock(_time), new AwtrixAddress { BaseTopic = "awtrix/clock1" },
                _awtrix.Object, _timer.Object, config, _planner.Object);
            app.RefreshDelay = _delays.Delay;
            return app;
        }

        private void RaiseSecond() =>
            _timer.Raise(t => t.SecondChanged += null, this, new ClockTickEventArgs(_time.GetLocalNow().DateTime));

        private int PlannerCalls => _planner.Invocations.Count;

        private static DateTimeOffset OnlyDeparture(TripTimerApp app) => Assert.Single(app.NextDepartures).Origin.Time;

        private void VerifyAppUpdates(Times times) =>
            _awtrix.Verify(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), AppNames.TripTimerApp, It.IsAny<AwtrixAppMessage>()), times);

        private void VerifyNotifies(Times times) =>
            _awtrix.Verify(a => a.Notify(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixAppMessage>()), times);

        private void VerifyNoErrorsLogged() =>
            _logger.Verify(x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);

        [Theory]
        [InlineData(0, 120)]
        [InlineData(1, 30)]
        [InlineData(2, 60)]
        [InlineData(3, 120)]
        [InlineData(40, 120)]
        public void NextRefreshDelay_BacksOffAfterFailures_UpToTheRefreshInterval(int consecutiveFailures, int expectedSeconds)
        {
            Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), TripTimerApp.NextRefreshDelay(consecutiveFailures));
        }

        [Fact]
        public async Task Refresh_AfterTheInterval_ReplacesTheDepartures()
        {
            var app = CreateApp();
            app.ExecuteNow();

            var wait = await _delays.NextAsync();
            Assert.Equal(TripTimerApp.RefreshInterval, wait.Delay);
            Assert.Equal(Departure, OnlyDeparture(app));

            _plannerResult = _ => Task.FromResult(Departures(Departure.AddMinutes(8))); // the 06:41 is now 8 minutes late
            wait.Release();

            Assert.Equal(TripTimerApp.RefreshInterval, (await _delays.NextAsync()).Delay);
            Assert.Equal(Departure.AddMinutes(8), OnlyDeparture(app));
            Assert.Equal(2, PlannerCalls);
            await app.DisposeAsync();
        }

        [Fact]
        public async Task FailedRefresh_KeepsTheLastGoodList_AndBacksOff()
        {
            var app = CreateApp();
            app.ExecuteNow();
            var wait = await _delays.NextAsync();

            _plannerResult = _ => Task.FromException<List<TripSummary>>(new HttpRequestException("503"));
            wait.Release();
            wait = await _delays.NextAsync();
            Assert.Equal(TimeSpan.FromSeconds(30), wait.Delay);
            Assert.Equal(Departure, OnlyDeparture(app));

            wait.Release();
            wait = await _delays.NextAsync();
            Assert.Equal(TimeSpan.FromSeconds(60), wait.Delay);
            Assert.Equal(Departure, OnlyDeparture(app));

            _plannerResult = _ => Task.FromResult(Departures(Departure.AddMinutes(4)));
            wait.Release();
            Assert.Equal(TripTimerApp.RefreshInterval, (await _delays.NextAsync()).Delay);
            Assert.Equal(Departure.AddMinutes(4), OnlyDeparture(app));

            VerifyNoErrorsLogged();
            await app.DisposeAsync();
        }

        [Fact]
        public async Task InitialQueryFailure_KeepsTheWindowOpen_UntilARetrySucceeds()
        {
            // CR-25: a transient failure at activation used to lose the whole window
            _plannerResult = _ => Task.FromException<List<TripSummary>>(new TaskCanceledException("HttpClient timeout"));
            var app = CreateApp();
            app.ExecuteNow();

            var wait = await _delays.NextAsync();
            Assert.Equal(TripTimerApp.RetryInterval, wait.Delay);

            RaiseSecond();
            Assert.False(app.LastRun.IsCompleted); // nothing loaded yet: the empty list does not end the window
            VerifyAppUpdates(Times.Never());

            _plannerResult = _ => Task.FromResult(Departures(Departure));
            wait.Release();
            Assert.Equal(TripTimerApp.RefreshInterval, (await _delays.NextAsync()).Delay);

            RaiseSecond();
            VerifyAppUpdates(Times.Once());
            VerifyNoErrorsLogged();
            await app.DisposeAsync();
        }

        [Fact]
        public async Task ExhaustedList_IsReQueriedOnce_BeforeTheWindowEnds()
        {
            var app = CreateApp();
            app.ExecuteNow();
            await _delays.NextAsync();
            _time.Advance(Departure - Now + TimeSpan.FromMinutes(1)); // 06:42: the only alarm has passed; still inside ActiveTime

            RaiseSecond();

            await app.LastRun.WaitAsync(Guard);
            Assert.Equal(2, PlannerCalls); // activation + one re-query that still found nothing in the future
            VerifyAppUpdates(Times.Never());
            VerifyNoErrorsLogged();
        }

        [Fact]
        public async Task ExhaustedList_ReQueryFindsALaterService_KeepsCountingDown()
        {
            var app = CreateApp();
            app.ExecuteNow();
            await _delays.NextAsync();
            _time.Advance(Departure - Now + TimeSpan.FromMinutes(1));
            _plannerResult = _ => Task.FromResult(Departures(Departure.AddMinutes(15)));

            RaiseSecond(); // finds nothing, re-queries, keeps the window

            Assert.False(app.LastRun.IsCompleted);
            Assert.Equal(Departure.AddMinutes(15), OnlyDeparture(app));

            RaiseSecond(); // counts down to the later service
            VerifyAppUpdates(Times.Once());
            await app.DisposeAsync();
        }

        [Fact]
        public async Task EndingTheWindow_CancelsTheInFlightQuery_AndIgnoresItsLateResult()
        {
            var app = CreateApp();
            app.ExecuteNow();
            var wait = await _delays.NextAsync();

            var inFlight = new TaskCompletionSource<List<TripSummary>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _plannerResult = token => inFlight.Task.WaitAsync(token);
            wait.Release();
            Assert.True(SpinWait.SpinUntil(() => PlannerCalls == 2, Guard));

            await app.DisposeAsync();

            Assert.True(_plannerTokens.Last().IsCancellationRequested);
            inFlight.TrySetResult(Departures(Departure.AddMinutes(30)));
            Assert.Equal(Departure, OnlyDeparture(app));
            Assert.Equal(0, _delays.Pending);
            VerifyNoErrorsLogged();
        }

        [Fact]
        public async Task SupersededWhileClearingTheSlot_DoesNotAnnounceOrQueryForTheOldWindow()
        {
            // WS4 review: a superseded activation could still publish "Starting trip timer" and call TfNSW
            var firstClear = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var clears = 0;
            _awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>()))
                .Returns(() => Interlocked.Increment(ref clears) == 1 ? firstClear.Task : Task.FromResult(true));
            var app = CreateApp();

            app.ExecuteNow();
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref clears) == 1, Guard));
            app.ExecuteNow(); // supersedes #1 while it is still clearing its slot
            firstClear.TrySetResult(true);

            await _delays.NextAsync(); // #2 is active and its refresh loop is waiting
            VerifyNotifies(Times.Once());
            Assert.Equal(1, PlannerCalls);
            VerifyNoErrorsLogged();
            await app.DisposeAsync();
        }

        [Fact]
        public async Task SupersededWhileAnnouncing_DoesNotQueryForTheOldWindow()
        {
            var firstNotify = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var notifies = 0;
            _awtrix.Setup(a => a.Notify(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixAppMessage>()))
                .Returns(() => Interlocked.Increment(ref notifies) == 1 ? firstNotify.Task : Task.FromResult(true));
            var app = CreateApp();

            app.ExecuteNow();
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref notifies) == 1, Guard));
            app.ExecuteNow(); // supersedes #1 while "Starting trip timer" is being published
            firstNotify.TrySetResult(true);

            await _delays.NextAsync();
            Assert.Equal(1, PlannerCalls);
            VerifyNoErrorsLogged();
            await app.DisposeAsync();
        }

        private sealed class ScriptedDelay
        {
            private readonly Channel<Step> _steps = Channel.CreateUnbounded<Step>();

            public int Pending => _steps.Reader.Count;

            public Task Delay(TimeSpan delay, CancellationToken token)
            {
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _steps.Writer.TryWrite(new Step(delay, gate));
                return gate.Task.WaitAsync(token);
            }

            public async Task<Step> NextAsync() => await _steps.Reader.ReadAsync().AsTask().WaitAsync(Guard);
        }

        private sealed record Step(TimeSpan Delay, TaskCompletionSource Gate)
        {
            public void Release() => Gate.TrySetResult();
        }
    }
}
