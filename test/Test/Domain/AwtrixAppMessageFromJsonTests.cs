using AwtrixSharpWeb.Domain;

namespace Test.Domain
{
    public class AwtrixAppMessageFromJsonTests
    {
        [Fact]
        public void RoundTrips_NestedValues()
        {
            var json = "{\"text\":\"hi\",\"effect\":\"Plasma\",\"effectSpeed\":1.5,\"palette\":[[255,0,0],[0,0,255]],\"scroll\":{\"mode\":\"bounce\"},\"hold\":true}";

            Assert.True(AwtrixAppMessage.TryFromJson(json, out var message, out var error));

            Assert.Null(error);
            Assert.Equal("hi", message.Text);
            Assert.Equal(
                "{\"text\":\"hi\",\"effect\":\"Plasma\",\"effectSpeed\":1.5,\"hold\":true,\"palette\":[[255,0,0],[0,0,255]],\"scroll\":{\"mode\":\"bounce\"}}",
                message.ToJson());
        }

        [Fact]
        public void BlankText_IsAccepted()
        {
            Assert.True(AwtrixAppMessage.TryFromJson("{\"text\":\"\"}", out var message, out _));

            Assert.Equal("", message.Text);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("[]")]
        [InlineData("\"x\"")]
        public void NonObject_IsRejected(string? json)
        {
            Assert.False(AwtrixAppMessage.TryFromJson(json, out var message, out var error));

            Assert.Empty(message);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }

        [Fact]
        public void InvalidJson_ReportsParserMessage()
        {
            Assert.False(AwtrixAppMessage.TryFromJson("{\"text\":", out _, out var error));

            Assert.Contains("JSON", error!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ToJson_ExcludedKeys_StillApply()
        {
            AwtrixAppMessage.TryFromJson("{\"text\":\"a\",\"hold\":true}", out var message, out _);

            Assert.Equal("{\"text\":\"a\"}", message.ToJson(new[] { "hold" }));
        }
    }
}
