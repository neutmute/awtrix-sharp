using AwtrixSharpWeb.Domain;
using System.Globalization;

namespace Test.Domain
{
    /// <summary>ToJson is a plain typed serialize: NG validates types and rejects unknown keys (spec §1).</summary>
    public class AwtrixAppMessageJsonTests
    {
        [Fact]
        public void ToJson_EmptyMessage_ReturnsEmptyObject() => Assert.Equal("{}", new AwtrixAppMessage().ToJson());

        [Fact]
        public void ToJson_TextFirst_ThenKeysSorted()
        {
            var m = new AwtrixAppMessage().SetProgress(1).SetIcon("5").SetText("t");
            Assert.Equal("{\"text\":\"t\",\"icon\":\"5\",\"progress\":1}", m.ToJson());
        }

        [Fact]
        public void ToJson_TypedScalars()
        {
            var m = new AwtrixAppMessage().SetDurationMs(5000).SetHold().SetEffectSpeed(1.5).SetTextOffsetX(-2);
            Assert.Equal("{\"durationMs\":5000,\"effectSpeed\":1.5,\"hold\":true,\"textOffsetX\":-2}", m.ToJson());
        }

        [Fact]
        public void ToJson_Enums_UseNgNames()
        {
            var m = new AwtrixAppMessage().SetTextCase(TextCase.AsTyped).SetIconMode(IconMode.PushOnce).SetLifetimeExpiry(LifetimeExpiry.Mark);
            Assert.Equal("{\"iconMode\":\"pushOnce\",\"lifetimeExpiry\":\"mark\",\"textCase\":\"asTyped\"}", m.ToJson());
        }

        [Fact]
        public void ToJson_Arrays()
        {
            var m = new AwtrixAppMessage().SetLineChart(new[] { 1, 2, 3 }).SetProgressColor("255,0,0")
                .SetPalette(new[] { new[] { 255, 0, 0 }, new[] { 0, 255, 0 } });
            Assert.Equal("{\"lineChart\":[1,2,3],\"palette\":[[255,0,0],[0,255,0]],\"progressColor\":[255,0,0]}", m.ToJson());
        }

        [Fact]
        public void ToJson_Scroll_IsNested()
        {
            Assert.Equal("{\"scroll\":{\"speed\":100}}", new AwtrixAppMessage().SetScrollSpeed(100).ToJson());
        }

        [Fact]
        public void ToJson_Fragments_UseTextAndColor_AndOmitNullColor()
        {
            var m = new AwtrixAppMessage().SetText(new[] { new TextFragment("12:00", "#00FF00"), new TextFragment(" ->41"), new TextFragment("x", "1,2,3") });
            Assert.Equal("{\"text\":[{\"text\":\"12:00\",\"color\":\"#00FF00\"},{\"text\":\" ->41\"},{\"text\":\"x\",\"color\":[1,2,3]}]}", m.ToJson());
        }

        [Fact]
        public void ToJson_DoesNotEscapeNonAscii()
        {
            Assert.Equal("{\"text\":\"→ café\"}", new AwtrixAppMessage().SetText("→ café").ToJson());
        }

        [Fact]
        public void ToJson_ExcludedKeys_AreOmitted()
        {
            var m = new AwtrixAppMessage().SetText("t").SetHold().SetStack(false).SetLifetimeMs(1);
            Assert.Equal("{\"text\":\"t\",\"lifetimeMs\":1}", m.ToJson("hold", "stack"));
            Assert.Equal("{\"text\":\"t\",\"hold\":true,\"lifetimeMs\":1,\"stack\":false}", m.ToJson());
        }

        [Fact]
        public void ToJson_UnderCommaDecimalCulture_UsesInvariantFormat()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.Equal("{\"effectSpeed\":0.5}", new AwtrixAppMessage().SetEffectSpeed(0.5).ToJson());
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public void TryParseIntArray_Valid_ReturnsValues()
        {
            Assert.True(AwtrixAppMessage.TryParseIntArray(" 1, -2 ,3", out var values));
            Assert.Equal(new[] { 1, -2, 3 }, values);
        }

        [Fact]
        public void TryParseIntArray_Invalid_ReturnsFalse()
        {
            Assert.False(AwtrixAppMessage.TryParseIntArray("1,x", out _));
            Assert.False(AwtrixAppMessage.TryParseIntArray("", out _));
        }

        [Fact]
        public void TryParseIntMatrix_ValidAndInvalid()
        {
            Assert.True(AwtrixAppMessage.TryParseIntMatrix("1,2;3,4", out var matrix));
            Assert.Equal(new[] { new[] { 1, 2 }, new[] { 3, 4 } }, matrix);
            Assert.False(AwtrixAppMessage.TryParseIntMatrix("1,2;x", out _));
        }
    }
}
