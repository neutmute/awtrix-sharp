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
            _planner.Setup(p => p.GetNextDepartures(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
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
        public async Task Tick_WhenAppUpdateFaults_DoesNotPropagate()
        {
            // Migrated from the WS1 tick test: the timer loop never sees a publish failure
            var app = CreateApp();
            app.ExecuteNow();
            _awtrix.Setup(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()))
                .ThrowsAsync(new HttpRequestException("device offline"));

            var exception = Record.Exception(RaiseSecond);

            Assert.Null(exception);
            _awtrix.Verify(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()), Times.Once);
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
