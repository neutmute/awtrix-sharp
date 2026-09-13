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
