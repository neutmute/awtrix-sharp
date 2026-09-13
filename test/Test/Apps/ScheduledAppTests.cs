using AwtrixSharpWeb.Apps;
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Test.Apps
{
    /// <summary>
    /// Minimal concrete scheduled app used to exercise ScheduledApp&lt;TConfig&gt;.
    /// ActivateScheduledWork completes immediately unless configured to wait for cancellation,
    /// which lets tests avoid depending on the real cron wake-up (real Task.Delay) path.
    /// </summary>
    internal class TestScheduledApp : ScheduledApp<ScheduledAppConfig>
    {
        public int ActivateCallCount { get; private set; }
        public bool WaitForCancellationOnActivate { get; set; }

        /// <summary>
        /// Optional per-activation teardown step (index = activation number, 0-based), awaited after
        /// cancellation — simulates slow deactivation I/O such as TripTimerApp's AppClear.
        /// </summary>
        public Func<int, Task>? Teardown { get; set; }

        public TestScheduledApp(ILogger logger, IClock clock, AwtrixAddress awtrixAddress, IAwtrixService awtrixService, ScheduledAppConfig config)
            : base(logger, clock, awtrixAddress, awtrixService, config)
        {
        }

        protected override async Task ActivateScheduledWork(CancellationTokenSource cts)
        {
            var activation = ActivateCallCount++;
            if (WaitForCancellationOnActivate)
            {
                await WaitForCancellation(cts.Token);
                if (Teardown != null)
                {
                    await Teardown(activation);
                }
            }
        }

        public static Task PublicWaitForCancellation(CancellationToken token) => WaitForCancellation(token);
    }

    /// <summary>
    /// Awaitable that parks the awaiting continuation until the test resumes it on the test's own
    /// thread, so everything up to the next real async point runs synchronously inside Resume().
    /// </summary>
    internal sealed class ManualGate
    {
        private readonly TaskCompletionSource _parked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Action? _continuation;

        public Task Parked => _parked.Task;

        public Awaiter GetAwaiter() => new(this);

        public void Resume() => Interlocked.Exchange(ref _continuation, null)!.Invoke();

        public readonly struct Awaiter : System.Runtime.CompilerServices.INotifyCompletion
        {
            private readonly ManualGate _gate;
            public Awaiter(ManualGate gate) => _gate = gate;
            public bool IsCompleted => false;
            public void OnCompleted(Action continuation)
            {
                _gate._continuation = continuation;
                _gate._parked.TrySetResult();
            }
            public void GetResult() { }
        }
    }

    public class ScheduledAppTests
    {
        private Mock<ILogger> _mockLogger;
        private Mock<IAwtrixService> _mockAwtrixService;
        private AwtrixAddress _address;
        private MockClock _clock;

        private TestScheduledApp CreateSut(string cronSchedule = "* * * * *", string type = null)
        {
            _mockLogger = new Mock<ILogger>();
            _mockAwtrixService = new Mock<IAwtrixService>();
            _address = new AwtrixAddress { BaseTopic = "test/base/topic" };
            _clock = new MockClock(DateTimeOffset.Now);

            _mockAwtrixService.Setup(x => x.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>())).ReturnsAsync(true);
            _mockAwtrixService.Setup(x => x.Dismiss(It.IsAny<AwtrixAddress>())).ReturnsAsync(true);

            var config = new ScheduledAppConfig
            {
                CronSchedule = cronSchedule,
                ActiveTime = TimeSpan.FromMinutes(5)
            };
            if (type != null)
            {
                config.WithName(type);
            }

            return new TestScheduledApp(_mockLogger.Object, _clock, _address, _mockAwtrixService.Object, config);
        }

        [Fact]
        public void Init_WithValidCron_DoesNotThrow()
        {
            var sut = CreateSut();

            var ex = Record.Exception(() => sut.Init());

            Assert.Null(ex);
            sut.Dispose();
        }

        [Fact]
        public void Init_WithInvalidCron_Throws()
        {
            var sut = CreateSut(cronSchedule: "not a cron expression");

            Assert.ThrowsAny<Exception>(() => sut.Init());
        }

        [Fact]
        public async Task ExecuteNow_InvokesActivateScheduledWork()
        {
            var sut = CreateSut(type: "MyApp");

            sut.ExecuteNow();

            // Allow the fire-and-forget WakeUp() task to progress past its synchronous prefix.
            await Task.Delay(50);

            Assert.True(sut.ActivateCallCount >= 1);

            sut.Dispose();
        }

        [Fact]
        public void Dispose_CancelsPendingWorkAndClearsApp()
        {
            var sut = CreateSut(type: "MyApp");
            sut.WaitForCancellationOnActivate = true;

            sut.ExecuteNow();

            sut.Dispose();

            _mockAwtrixService.Verify(x => x.Dismiss(_address), Times.AtLeastOnce);
        }

        private static CancellationTokenSource? CurrentCts(TestScheduledApp app) =>
            (CancellationTokenSource?)typeof(TestScheduledApp)
                .GetField("_cts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy)!
                .GetValue(app);

        [Fact]
        public async Task ExecuteNow_DuringActiveWindowWithSlowTeardown_NewActivationSurvivesOldTeardown()
        {
            // Run on the thread pool (no SynchronizationContext, as in production); xUnit's context would
            // serialise the continuations and hide the race.
            await Task.Run(async () =>
            {
                var sut = CreateSut(type: "MyApp");
                sut.WaitForCancellationOnActivate = true;
                var oldTeardown = new ManualGate();
                sut.Teardown = i => i == 0 ? AwaitGate(oldTeardown) : Task.CompletedTask;

                sut.ExecuteNow();                       // activation #1 now waiting for cancellation
                Assert.Equal(1, sut.ActivateCallCount);

                sut.ExecuteNow();                       // double-click: supersedes #1, activation #2 starts
                Assert.Equal(2, sut.ActivateCallCount);
                var second = CurrentCts(sut)!;

                // #1's teardown runs asynchronously and parks on its slow deactivation I/O.
                await oldTeardown.Parked.WaitAsync(TimeSpan.FromSeconds(5));

                // Finish #1's teardown synchronously on this thread (through WakeUp's finally).
                oldTeardown.Resume();

                Assert.False(second.IsCancellationRequested, "old activation's teardown cancelled the new activation");
                Assert.Same(second, CurrentCts(sut));

                sut.Dispose();
            });
        }

        [Fact]
        public async Task Dispose_DuringActiveWindow_TeardownDoesNotRescheduleOrTouchDisposedCts()
        {
            await Task.Run(async () =>
            {
                var sut = CreateSut(type: "MyApp");
                sut.WaitForCancellationOnActivate = true;
                var teardown = new ManualGate();
                sut.Teardown = _ => AwaitGate(teardown);

                sut.ExecuteNow();
                sut.Dispose();

                await teardown.Parked.WaitAsync(TimeSpan.FromSeconds(5));
                teardown.Resume();

                // Pre-fix: WakeUp's finally called Cancel() on the already-disposed CTS (ObjectDisposedException)
                // and left the disposed instance in place; the app must stay torn down.
                Assert.Null(CurrentCts(sut));
            });
        }

        private static async Task AwaitGate(ManualGate gate) => await gate;

        [Fact]
        public async Task WaitForCancellation_CompletesOnlyAfterTokenIsCancelled()
        {
            using var cts = new CancellationTokenSource();

            var task = TestScheduledApp.PublicWaitForCancellation(cts.Token);

            Assert.False(task.IsCompleted);

            cts.Cancel();

            var completed = await Task.WhenAny(task, Task.Delay(1000));
            Assert.Same(task, completed);
        }
    }
}
