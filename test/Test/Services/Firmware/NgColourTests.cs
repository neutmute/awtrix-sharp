using AwtrixSharpWeb.Services.Firmware;

namespace Test.Services.Firmware
{
    public class NgColourTests
    {
        [Theory]
        [InlineData("#FF0000", "#FF0000")]
        [InlineData("FF0000", "#FF0000")]
        [InlineData("ff00aa", "#ff00aa")]
        [InlineData("#F00", "#F00")]
        public void Normalise_HexStrings(string input, string expected) => Assert.Equal(expected, NgColour.Normalise(input));

        [Fact]
        public void Normalise_CommaSeparatedInts_BecomesArray()
        {
            Assert.Equal(new[] { 255, 0, 8 }, Assert.IsType<int[]>(NgColour.Normalise("255,0,8")));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("red")]
        [InlineData("1,2")]
        [InlineData("#GG0000")]
        public void Normalise_Invalid_ReturnsNull(string input) => Assert.Null(NgColour.Normalise(input));
    }
}
