using AwtrixSharpWeb.Domain;
using System.Globalization;

namespace Test.Domain
{
    /// <summary>
    /// CR-34: Awtrix expects arrays for line/bar/progressC/progressBC/gradient, and numbers must not follow
    /// the host culture. Stored dictionary values stay unchanged (see AwtrixAppMessageBuilderTests).
    /// </summary>
    public class AwtrixAppMessageJsonTests
    {
        [Fact]
        public void ToJson_Line_EmitsJsonArray()
        {
            var message = new AwtrixAppMessage().SetLine(new[] { 1, 2, 3 });

            Assert.Equal("{\"line\":[1,2,3]}", message.ToJson());
            Assert.Equal("1,2,3", message["line"]);
        }

        [Fact]
        public void ToJson_Bar_EmitsJsonArray()
        {
            var message = new AwtrixAppMessage().SetBar(new[] { 4, -5, 6, 7 });

            Assert.Equal("{\"bar\":[4,-5,6,7]}", message.ToJson());
        }

        [Fact]
        public void ToJson_ProgressColours_EmitJsonArrays()
        {
            var message = new AwtrixAppMessage()
                .SetProgressC(new[] { 255, 0, 0 })
                .SetProgressBC(new[] { 0, 0, 255 });

            Assert.Equal("{\"progressC\":[255,0,0],\"progressBC\":[0,0,255]}", message.ToJson());
        }

        [Fact]
        public void ToJson_Gradient_EmitsNestedJsonArrays()
        {
            var message = new AwtrixAppMessage().SetGradient(new[] { new[] { 255, 0, 0 }, new[] { 0, 255, 0 } });

            Assert.Equal("{\"gradient\":[[255,0,0],[0,255,0]]}", message.ToJson());
        }

        [Fact]
        public void ToJson_UnparsableArrayValue_FallsBackToString()
        {
            var message = new AwtrixAppMessage();
            message["bar"] = "not,numbers";

            Assert.Equal("{\"bar\":\"not,numbers\"}", message.ToJson());
        }

        [Fact]
        public void ToJson_ScalarsRemainStrings()
        {
            // Deliberately unchanged by WS5 (spec non-goal): scalars are still emitted as JSON strings.
            var message = new AwtrixAppMessage().SetDuration(5).SetRainbow();

            Assert.Equal("{\"duration\":\"5\",\"rainbow\":\"true\"}", message.ToJson());
        }

        [Fact]
        public void DoubleSetters_UnderCommaDecimalCulture_UseInvariantFormat()
        {
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                var message = new AwtrixAppMessage().SetBlinkText(0.5).SetFadeText(1.5);

                Assert.Equal("0.5", message["blinkText"]);
                Assert.Equal("1.5", message["fadeText"]);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Theory]
        [InlineData("1,2,3", new[] { 1, 2, 3 })]
        [InlineData(" 7 , 8 ", new[] { 7, 8 })]
        [InlineData("-1", new[] { -1 })]
        public void TryParseIntArray_Valid_ReturnsValues(string input, int[] expected)
        {
            Assert.True(AwtrixAppMessage.TryParseIntArray(input, out var result));
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1,,2")]
        [InlineData("1,a")]
        [InlineData("1.5")]
        public void TryParseIntArray_Invalid_ReturnsFalse(string? input)
        {
            Assert.False(AwtrixAppMessage.TryParseIntArray(input, out _));
        }

        [Fact]
        public void TryParseIntMatrix_ValidAndInvalid()
        {
            Assert.True(AwtrixAppMessage.TryParseIntMatrix("255,0,0;0,255,0", out var matrix));
            Assert.Equal(new[] { new[] { 255, 0, 0 }, new[] { 0, 255, 0 } }, matrix);

            Assert.False(AwtrixAppMessage.TryParseIntMatrix("255,0,0;red", out _));
            Assert.False(AwtrixAppMessage.TryParseIntMatrix("", out _));
        }
    }
}
