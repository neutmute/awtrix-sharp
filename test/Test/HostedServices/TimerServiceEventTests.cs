using System.Reflection;
using AwtrixSharpWeb.HostedServices;
using Microsoft.Extensions.Logging.Abstractions;

namespace Test.HostedServices
{
    /// <summary>
    /// Exercises TimerService's second/minute change detection deterministically by
    /// invoking the private CheckTimeChange callback directly (via reflection) with a
    /// controlled "_lastTime" seed, rather than waiting on the real 100ms polling timer.
    /// TimerService has no injectable clock, so DateTime.Now is still used for "now" -
    /// tests seed "_lastTime" far enough away (by 30 seconds/minutes) that the
    /// comparison result is deterministic regardless of the exact instant "now" resolves to.
    /// </summary>
    public class TimerServiceEventTests
    {
        private static TimerService CreateService()
        {
            return new TimerService(NullLogger<TimerService>.Instance);
        }

        private static void SetLastTime(TimerService service, DateTime value)
        {
            var field = typeof(TimerService).GetField("_lastTime", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            field!.SetValue(service, value);
        }

        private static void InvokeCheckTimeChange(TimerService service)
        {
            var method = typeof(TimerService).GetMethod("CheckTimeChange", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            method!.Invoke(service, new object?[] { null });
        }

        [Fact]
        public void CheckTimeChange_WhenSecondDiffers_RaisesSecondChanged()
        {
            var service = CreateService();
            var now = DateTime.Now;
            // Seed a "last" time whose second (and minute, to isolate the assertion)
            // is guaranteed to differ from "now" by picking values 30 apart (mod 60).
            var last = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, (now.Second + 30) % 60, now.Kind);
            SetLastTime(service, last);

            ClockTickEventArgs? raised = null;
            service.SecondChanged += (s, e) => raised = e;

            InvokeCheckTimeChange(service);

            Assert.NotNull(raised);
        }

        [Fact]
        public void CheckTimeChange_WhenSecondAndMinuteDiffer_RaisesBothEvents()
        {
            var service = CreateService();
            var now = DateTime.Now;
            var last = new DateTime(now.Year, now.Month, now.Day, now.Hour, (now.Minute + 30) % 60, (now.Second + 30) % 60, now.Kind);
            SetLastTime(service, last);

            bool secondRaised = false;
            bool minuteRaised = false;
            service.SecondChanged += (s, e) => secondRaised = true;
            service.MinuteChanged += (s, e) => minuteRaised = true;

            InvokeCheckTimeChange(service);

            Assert.True(secondRaised);
            Assert.True(minuteRaised);
        }

        [Fact]
        public void CheckTimeChange_WhenSameSecond_DoesNotRaiseEvents()
        {
            var service = CreateService();
            var now = DateTime.Now;
            // Seed _lastTime to exactly the current trimmed second - no change should be detected.
            SetLastTime(service, new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Kind));

            bool secondRaised = false;
            bool minuteRaised = false;
            service.SecondChanged += (s, e) => secondRaised = true;
            service.MinuteChanged += (s, e) => minuteRaised = true;

            InvokeCheckTimeChange(service);

            Assert.False(secondRaised);
            Assert.False(minuteRaised);
        }

        [Fact]
        public void CheckTimeChange_UpdatesLastTime_AfterSecondChange()
        {
            var service = CreateService();
            var now = DateTime.Now;
            var last = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, (now.Second + 30) % 60, now.Kind);
            SetLastTime(service, last);

            InvokeCheckTimeChange(service);

            var field = typeof(TimerService).GetField("_lastTime", BindingFlags.NonPublic | BindingFlags.Instance);
            var updated = (DateTime)field!.GetValue(service)!;

            Assert.NotEqual(last, updated);
        }

        [Fact]
        public void Dispose_DoesNotThrow_WhenTimerNeverStarted()
        {
            var service = CreateService();

            var exception = Record.Exception(() => service.Dispose());

            Assert.Null(exception);
        }

        [Fact]
        public async Task StartAsync_ThenStopAsync_CompletesWithoutHanging()
        {
            var service = CreateService();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            await service.StartAsync(cts.Token);
            await service.StopAsync(CancellationToken.None);

            service.Dispose();
        }
    }
}
