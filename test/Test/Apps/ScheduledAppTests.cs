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
            Func<FakeTimeProvider, TimeProvider>? wrapTime = null,
            TimeSpan? activeTime = null)
        {
            _time = new FakeTimeProvider(Start);
            _logger = new Mock<ILogger>();
            _awtrix = new Mock<IAwtrixService>();
            _awtrix.Setup(x => x.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>())).ReturnsAsync(true);

            var config = new ScheduledAppConfig { CronSchedule = cron };
            if (setActiveTime)
            {
                config.ActiveTime = activeTime ?? ActiveTime;
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
        public async Task ExecuteNow_WhileCronActivationIsActive_EndsAfterActiveTime_AndTheCronFiresAgain()
        {
            // WS3 review M1: a manual start on an active app used to begin an activation that never ended,
            // and the cron never fired again until restart
            var sut = CreateSut();
            await sut.InitAsync();
            _time.Advance(TimeSpan.FromMinutes(1));
            var cron = await Next(sut.Activated);

            sut.ExecuteNow();
            var manual = await Next(sut.Activated);

            Assert.True(cron.IsEnded);
            Assert.Equal(ActivationTrigger.Manual, manual.Trigger);

            _time.Advance(ActiveTime);
            await sut.LastRun.WaitAsync(Guard);

            Assert.True(manual.IsEnded);
            Assert.Equal(Eight.AddDays(1), sut.NextWakeUp);

            _time.Advance(Eight.AddDays(1) - _time.GetUtcNow());
            var tomorrow = await Next(sut.Activated);

            Assert.Equal(ActivationTrigger.Cron, tomorrow.Trigger);
            Assert.Equal(3, tomorrow.Number);
            VerifyErrorsLogged(Times.Never());
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
        public async Task DisposeAsync_AwaitsTheEndedActivationsTeardown_BeforeTheFinalClear()
        {
            // WS3 review M4: DisposeAsync used to return without awaiting the cancelled run's teardown
            var sut = CreateSut();
            var teardown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            sut.DeactivateWork = _ => teardown.Task;
            sut.ExecuteNow();
            await Next(sut.Activated);

            var dispose = sut.DisposeAsync().AsTask();
            await Next(sut.Deactivating);

            Assert.False(dispose.IsCompleted);
            _awtrix.Verify(x => x.AppClear(_address, "MyApp"), Times.Once); // activation start only

            teardown.SetResult();
            await dispose.WaitAsync(Guard);

            _awtrix.Verify(x => x.AppClear(_address, "MyApp"), Times.Exactly(3)); // start + end of run + final clear
            VerifyErrorsLogged(Times.Never());
        }

        [Fact]
        public async Task Dispose_DuringActiveRun_ReturnsWithoutWaitingForTeardown_AndNeverRearms()
        {
            // Migrated from the WS1/WS3 sync-dispose tests: Dispose never blocks, and the teardown that finishes
            // afterwards neither re-arms the schedule nor touches a disposed token source
            var sut = CreateSut();
            await sut.InitAsync();
            var teardown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            sut.DeactivateWork = _ => teardown.Task;
            sut.ExecuteNow();
            var activation = await Next(sut.Activated);

            await Task.Run(() => sut.Dispose()).WaitAsync(Guard);

            Assert.True(activation.IsEnded);
            await Next(sut.Deactivating);
            teardown.SetResult();
            await sut.LastRun.WaitAsync(Guard);

            Assert.Null(sut.NextWakeUp);
            sut.ExecuteNow();
            Assert.Equal(new[] { "activate#1", "deactivate#1" }, sut.Events);
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
            // Timers reject delays over ~49.7 days; a yearly cron used to fail once and never run
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

        [Fact]
        public async Task ExecuteNow_WithZeroActiveTime_EndsImmediatelyWithoutActivating_AndKeepsScheduling()
        {
            // Spec D4: zero ActiveTime ends at once (what cron runs effectively did before); logged as a warning
            var sut = CreateSut(activeTime: TimeSpan.Zero);
            await sut.InitAsync();

            sut.ExecuteNow();
            await sut.LastRun.WaitAsync(Guard);

            Assert.Empty(sut.Events);
            Assert.Equal(Eight, sut.NextWakeUp);
            VerifyErrorsLogged(Times.Never());
            await sut.DisposeAsync();
        }

        [Fact]
        public async Task ScheduledActivation_Complete_IsIdempotent_AndTokenStaysReadableAfterRelease()
        {
            // Replaces the old wait-for-cancellation helper test: Ended completes only once the window ends
            var time = new FakeTimeProvider(Start);
            using var lifetime = new CancellationTokenSource();
            var activation = new ScheduledActivation(1, ActivationTrigger.Manual, Start, ActiveTime, time, lifetime.Token);
            Assert.False(activation.IsEnded);
            Assert.False(activation.Ended.IsCompleted);

            activation.Complete();
            activation.Complete();

            Assert.True(activation.IsEnded);
            await activation.Ended.WaitAsync(Guard);

            activation.Release();
            var exception = Record.Exception(() => activation.Complete());
            Assert.Null(exception);
            Assert.True(activation.Token.IsCancellationRequested);
        }

        [Fact]
        public async Task ScheduledActivation_EndsWhenActiveTimeElapses_OrTheLifetimeIsCancelled()
        {
            var time = new FakeTimeProvider(Start);
            using var lifetime = new CancellationTokenSource();
            var timed = new ScheduledActivation(1, ActivationTrigger.Cron, Start, ActiveTime, time, lifetime.Token);
            var other = new ScheduledActivation(2, ActivationTrigger.Cron, Start, TimeSpan.FromDays(1), time, lifetime.Token);

            time.Advance(ActiveTime - TimeSpan.FromSeconds(1));
            Assert.False(timed.IsEnded);
            time.Advance(TimeSpan.FromSeconds(1));
            await timed.Ended.WaitAsync(Guard);

            Assert.False(other.IsEnded);
            lifetime.Cancel();
            await other.Ended.WaitAsync(Guard);
        }

        /// <summary>Every activation registers a token callback that throws, then reports itself once registered.</summary>
        private static ChannelReader<ScheduledActivation> RegisterThrowingTokenCallback(TestScheduledApp sut)
        {
            var registered = Channel.CreateUnbounded<ScheduledActivation>();
            sut.ActivateWork = activation =>
            {
                activation.Token.Register(() => throw new InvalidOperationException("token callback failure"));
                registered.Writer.TryWrite(activation);
                return Task.CompletedTask;
            };
            return registered.Reader;
        }

        private void VerifyCallbackWarningLogged() =>
            _logger.Verify(x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("MyApp")),
                It.Is<Exception?>(ex => ex is AggregateException),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);

        [Fact]
        public async Task ThrowingTokenCallback_WhenActiveTimeElapses_IsLogged_AndTheNextCronActivationStillHappens()
        {
            // WS4 review m1: a callback exception escaping cancellation used to halt the schedule until restart
            var sut = CreateSut();
            var registered = RegisterThrowingTokenCallback(sut);
            await sut.InitAsync();

            _time.Advance(TimeSpan.FromMinutes(1));
            var first = await Next(registered);

            Assert.Null(Record.Exception(() => _time.Advance(ActiveTime)));
            await sut.LastRun.WaitAsync(Guard);
            Assert.True(first.IsEnded);
            Assert.Equal(Eight.AddDays(1), sut.NextWakeUp);

            _time.Advance(Eight.AddDays(1) - _time.GetUtcNow());
            var second = await Next(registered);
            Assert.Equal((2, ActivationTrigger.Cron), (second.Number, second.Trigger));

            await sut.DisposeAsync(); // the lifetime cancel meets the second activation's throwing callback
            VerifyCallbackWarningLogged();
            VerifyErrorsLogged(Times.Never());
        }

        [Fact]
        public async Task ThrowingTokenCallback_WhenSupersededByExecuteNow_DoesNotThrowToTheCaller_AndTheNewActivationRuns()
        {
            var sut = CreateSut();
            var registered = RegisterThrowingTokenCallback(sut);
            await sut.InitAsync();

            sut.ExecuteNow();
            var first = await Next(registered);

            Assert.Null(Record.Exception(() => sut.ExecuteNow()));
            var second = await Next(registered);

            Assert.True(first.IsEnded);
            Assert.Equal(2, second.Number);
            Assert.Equal(new[] { "activate#1", "deactivate#1", "activate#2" }, sut.Events);

            await sut.DisposeAsync();
            await sut.LastRun.WaitAsync(Guard);
            VerifyCallbackWarningLogged();
            VerifyErrorsLogged(Times.Never());
        }

        [Theory]
        [InlineData(-60, 0)]
        [InlineData(0, 0)]
        [InlineData(60, 60)]
        public void CancellationScope_ClampTimeout_ClampsNegativeToZero(int seconds, int expectedSeconds)
        {
            Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), CancellationScope.ClampTimeout(TimeSpan.FromSeconds(seconds)));
        }

        [Fact]
        public void CancellationScope_ClampTimeout_ClampsBeyondTheTimerLimit_AndCancelAfterDisposeDoesNotThrow()
        {
            Assert.Equal(CancellationScope.MaxTimeout, CancellationScope.ClampTimeout(TimeSpan.FromDays(200)));

            var scope = new CancellationScope(CancellationToken.None, TimeSpan.FromDays(200), new FakeTimeProvider(Start));
            scope.Dispose();

            Assert.Null(Record.Exception(() => scope.Cancel()));
            Assert.False(scope.IsCancellationRequested);
        }
    }
}
