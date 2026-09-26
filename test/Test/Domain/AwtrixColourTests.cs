using AwtrixSharpWeb.Domain;

namespace Test.Domain
{
    public class AwtrixColourTests
    {
        [Theory]
        [InlineData("#FF0000", "#FF0000")]
        [InlineData("#ff00aa", "#ff00aa")]
        [InlineData("#F00", "#F00")]
        [InlineData(" #F00 ", "#F00")]
        public void Parse_HexStrings_ReturnsStringWithHash(string input, string expected) => Assert.Equal(expected, AwtrixColour.Parse(input));

        [Fact]
        public void Parse_CommaSeparatedInts_ReturnsArray()
        {
            Assert.Equal(new[] { 255, 0, 8 }, Assert.IsType<int[]>(AwtrixColour.Parse("255,0,8")));
        }

        [Theory]
        [InlineData("FF0000")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("red")]
        [InlineData("1,2")]
        [InlineData("1,2,3,4")]
        [InlineData("256,0,0")]
        [InlineData("-1,0,0")]
        [InlineData("#GG0000")]
        public void Parse_Invalid_Throws(string input) => Assert.Throws<ArgumentException>(() => AwtrixColour.Parse(input));

        [Fact]
        public void Parse_Null_Throws() => Assert.Throws<ArgumentException>(() => AwtrixColour.Parse(null!));

        [Fact]
        public void TryParse_Invalid_ReturnsFalseAndNull()
        {
            Assert.False(AwtrixColour.TryParse("FF0000", out var colour));
            Assert.Null(colour);
        }

        [Fact]
        public void TryParse_Valid_ReturnsTrue()
        {
            Assert.True(AwtrixColour.TryParse("#123456", out var colour));
            Assert.Equal("#123456", colour);
        }
    }
}
