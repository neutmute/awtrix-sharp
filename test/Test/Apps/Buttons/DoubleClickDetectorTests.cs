using AwtrixSharpWeb.Apps.MqttRender;

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
    }
}
