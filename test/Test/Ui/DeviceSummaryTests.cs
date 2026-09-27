using AwtrixSharpWeb.Ui.Components;

namespace Test.Ui
{
    public class DeviceSummaryTests
    {
        [Fact]
        public void Describe_UsesVersionAndIp_WhenPresent()
        {
            var text = DeviceSummary.Describe("{\"version\":\"1.2.3\",\"ip\":\"10.0.0.5\",\"other\":1}", null);

            Assert.Equal("version 1.2.3, ip 10.0.0.5", text);
        }

        [Fact]
        public void Describe_FallsBackToLastSeen()
        {
            var at = new DateTimeOffset(2026, 9, 27, 3, 4, 5, TimeSpan.Zero);
            var expected = $"state received {at.ToLocalTime():HH:mm:ss}";

            Assert.Equal(expected, DeviceSummary.Describe("{\"foo\":1}", at));
        }

        [Fact]
        public void Describe_NothingKnown()
        {
            Assert.Equal("no state received yet", DeviceSummary.Describe(null, null));
            Assert.Equal("no state received yet", DeviceSummary.Describe("not json", null));
        }

        [Fact]
        public void Pretty_IndentsJson_AndReturnsInputWhenNotJson()
        {
            Assert.Contains("\n", DeviceSummary.Pretty("{\"a\":1}"));
            Assert.Equal("plain", DeviceSummary.Pretty("plain"));
            Assert.Equal("", DeviceSummary.Pretty(""));
        }
    }
}
