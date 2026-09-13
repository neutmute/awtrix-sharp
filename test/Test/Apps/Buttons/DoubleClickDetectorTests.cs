using AwtrixSharpWeb.Apps.MqttRender;
using Microsoft.Extensions.Time.Testing;

namespace Test.Apps.Buttons
{
    public class DoubleClickDetectorTests
    {
        [Fact]
        public void RegisterClick_FirstClick_ReturnsFalse()
        {
            var sut = new DoubleClickDetector();

            Assert.False(sut.RegisterClick());
        }

        [Fact]
        public void RegisterClick_TwoImmediateClicks_SecondIsDetectedAsDoubleClick()
        {
            // Default 300ms threshold - two calls executed back-to-back in-process take
            // microseconds, so this is comfortably within the threshold without needing a sleep.
            var sut = new DoubleClickDetector();

            var first = sut.RegisterClick();
            var second = sut.RegisterClick();

            Assert.False(first);
            Assert.True(second);
        }

        [Fact]
        public void RegisterClick_AfterDoubleClickDetected_ResetsState()
        {
            var sut = new DoubleClickDetector();

            sut.RegisterClick(); // first
            var second = sut.RegisterClick(); // double-click, resets lastClick to MinValue
            var third = sut.RegisterClick(); // should be treated as a fresh first click

            Assert.True(second);
            Assert.False(third);
        }

        [Fact]
        public void RegisterClick_WithNegativeThreshold_NeverReportsDoubleClick()
        {
            // A negative threshold makes "now - lastClick <= threshold" always false,
            // deterministically exercising the "not a double click" path without any real delay.
            var sut = new DoubleClickDetector(thresholdMilliseconds: -1);

            var first = sut.RegisterClick();
            var second = sut.RegisterClick();
            var third = sut.RegisterClick();

            Assert.False(first);
            Assert.False(second);
            Assert.False(third);
        }

        [Fact]
        public void RegisterClick_SecondClickExactlyAtThreshold_IsDoubleClick()
        {
            var time = new FakeTimeProvider();
            var sut = new DoubleClickDetector(300, time);

            Assert.False(sut.RegisterClick());
            time.Advance(TimeSpan.FromMilliseconds(300));

            Assert.True(sut.RegisterClick());
        }

        [Fact]
        public void RegisterClick_SecondClickJustAfterThreshold_IsNotDoubleClick()
        {
            var time = new FakeTimeProvider();
            var sut = new DoubleClickDetector(300, time);

            Assert.False(sut.RegisterClick());
            time.Advance(TimeSpan.FromMilliseconds(301));

            Assert.False(sut.RegisterClick());
        }

        [Fact]
        public void RegisterClick_WallClockJumpsBackwards_IsNotDoubleClick()
        {
            // DST end / NTP step: wall clock goes back an hour while real (monotonic) time moves 5 s.
            // With DateTime.Now, "now - last" was negative and therefore <= threshold => false double-click.
            var time = new SteppableTimeProvider();
            var sut = new DoubleClickDetector(300, time);

            Assert.False(sut.RegisterClick());
            time.Step(monotonic: TimeSpan.FromSeconds(5), wallClock: TimeSpan.FromHours(-1));

            Assert.False(sut.RegisterClick());
        }

        [Fact]
        public void RegisterClick_SlowThenQuick_SecondPairIsDoubleClick()
        {
            var time = new FakeTimeProvider();
            var sut = new DoubleClickDetector(300, time);

            Assert.False(sut.RegisterClick());
            time.Advance(TimeSpan.FromSeconds(2));
            Assert.False(sut.RegisterClick());
            time.Advance(TimeSpan.FromMilliseconds(100));

            Assert.True(sut.RegisterClick());
        }

        /// <summary>
        /// Wall clock and monotonic timestamp move independently.
        /// </summary>
        private sealed class SteppableTimeProvider : TimeProvider
        {
            private long _timestamp;
            private DateTimeOffset _utcNow = new(2026, 4, 5, 16, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow() => _utcNow;

            public override long GetTimestamp() => _timestamp;

            public override long TimestampFrequency => TimeSpan.TicksPerSecond;

            public void Step(TimeSpan monotonic, TimeSpan wallClock)
            {
                _timestamp += monotonic.Ticks;
                _utcNow += wallClock;
            }
        }
    }
}
