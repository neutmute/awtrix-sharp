using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services.Firmware;

namespace Test.Services.Firmware
{
    /// <summary>One test per row of the spec's key table, plus the precedence/kind/ordering rules.</summary>
    public class NgPayloadTranslatorTests
    {
        private static string App(AwtrixAppMessage m) => NgPayloadTranslator.Translate(m, NgPayloadKind.App).Json;
        private static string Notification(AwtrixAppMessage m) => NgPayloadTranslator.Translate(m, NgPayloadKind.Notification).Json;

        [Fact]
        public void Text_PlainString() => Assert.Equal("{\"text\":\"42\"}", App(new AwtrixAppMessage().SetText("42")));

        [Fact]
        public void Text_FragmentArray_RenamesKeysAndNormalisesColours()
        {
            var m = new AwtrixAppMessage().SetText("[{\"t\":\"07:10\",\"c\":\"00FF00\"},{\"t\":\" ->07:24\",\"c\":\"#FF0000\"}]");
            Assert.Equal("{\"text\":[{\"text\":\"07:10\",\"color\":\"#00FF00\"},{\"text\":\" ->07:24\",\"color\":\"#FF0000\"}]}", App(m));
        }

        [Fact]
        public void Text_MalformedFragmentArray_IsSentAsPlainString()
        {
            var m = new AwtrixAppMessage().SetText("[not json");
            Assert.Equal("{\"text\":\"[not json\"}", App(m));
        }

        [Fact]
        public void Text_ArrayOfNonObjects_IsSentAsPlainString()
        {
            var m = new AwtrixAppMessage().SetText("[1,2]");
            Assert.Equal("{\"text\":\"[1,2]\"}", App(m));
        }

        /// <summary>A non-string t/c anywhere makes the whole array fall back to the plain string (never a partial array).</summary>
        [Fact]
        public void Text_FragmentWithNonStringValues_IsSentAsPlainString()
        {
            var m = new AwtrixAppMessage().SetText("[{\"t\":5}]");
            Assert.Equal("{\"text\":\"[{\\\"t\\\":5}]\"}", App(m));
        }

        [Fact]
        public void Text_EmptyFragmentArray_IsEmptyArray() => Assert.Equal("{\"text\":[]}", App(new AwtrixAppMessage().SetText("[]")));

        [Fact]
        public void Text_FragmentWithoutText_OmitsTextKey()
        {
            var m = new AwtrixAppMessage().SetText("[{\"c\":\"#FF0000\"}]");
            Assert.Equal("{\"text\":[{\"color\":\"#FF0000\"}]}", App(m));
        }

        [Theory]
        [InlineData(0, "inherit")]
        [InlineData(1, "upper")]
        [InlineData(2, "asTyped")]
        public void TextCase_Words(int value, string expected) => Assert.Equal($"{{\"textCase\":\"{expected}\"}}", App(new AwtrixAppMessage().SetTextCase(value)));

        [Fact]
        public void TextCase_OutOfRange_IsDroppedAndReported()
        {
            var t = NgPayloadTranslator.Translate(new AwtrixAppMessage().SetTextCase(7), NgPayloadKind.App);
            Assert.Equal("{}", t.Json);
            Assert.Equal(new[] { "textCase" }, t.DroppedKeys);
        }

        [Fact]
        public void TopText_IsDroppedAndReported()
        {
            var t = NgPayloadTranslator.Translate(new AwtrixAppMessage().SetTopText(true), NgPayloadKind.App);
            Assert.Equal("{}", t.Json);
            Assert.Equal(new[] { "topText" }, t.DroppedKeys);
        }

        [Fact]
        public void Hold_And_Stack_NotificationOnly()
        {
            var m = new AwtrixAppMessage().SetHold().SetStack(false);
            Assert.Equal("{\"hold\":true,\"stack\":false}", Notification(m));
            var app = NgPayloadTranslator.Translate(m, NgPayloadKind.App);
            Assert.Equal("{}", app.Json);
            Assert.Empty(app.DroppedKeys); // dropped for kind, not unsupported
        }

        [Fact]
        public void Lifetime_AppOnly_Milliseconds()
        {
            var m = new AwtrixAppMessage().SetLifetime(120).SetLifetimeMode(1);
            Assert.Equal("{\"lifetimeExpiry\":\"mark\",\"lifetimeMs\":120000}", App(m));
            Assert.Equal("{}", Notification(m));
        }

        [Fact]
        public void LifetimeMode_Zero_IsRemove() => Assert.Equal("{\"lifetimeExpiry\":\"remove\"}", App(new AwtrixAppMessage().SetLifetimeMode(0)));

        [Fact]
        public void TextOffset_Center_Color_Background()
        {
            var m = new AwtrixAppMessage().SetTextOffset(3).SetCenter(false).SetColor("00FF00").SetBackground("#000011");
            Assert.Equal("{\"backgroundColor\":\"#000011\",\"textCenter\":false,\"textColor\":\"#00FF00\",\"textOffsetX\":3}", App(m));
        }

        [Fact]
        public void Gradient_BecomesPaletteAndOverridesColor()
        {
            var m = new AwtrixAppMessage().SetColor("#FFFFFF").SetGradient(new[] { new[] { 255, 0, 0 }, new[] { 0, 0, 255 } });
            Assert.Equal("{\"palette\":[[255,0,0],[0,0,255]],\"textColor\":\"palette\"}", App(m));
        }

        [Fact]
        public void Rainbow_True_BecomesRainbowPalette()
        {
            Assert.Equal("{\"palette\":\"Rainbow\",\"textColor\":\"palette\"}", App(new AwtrixAppMessage().SetRainbow()));
        }

        [Fact]
        public void Rainbow_False_IsSilentlyDropped()
        {
            var t = NgPayloadTranslator.Translate(new AwtrixAppMessage().SetRainbow(false).SetColor("#FFFFFF"), NgPayloadKind.App);
            Assert.Equal("{\"textColor\":\"#FFFFFF\"}", t.Json);
            Assert.Empty(t.DroppedKeys);
        }

        [Fact]
        public void PalettePrecedence_GradientOverRainbowOverEffectPalette()
        {
            var m = new AwtrixAppMessage().SetEffectPalette("Ocean").SetRainbow().SetGradient(new[] { new[] { 1, 2, 3 } });
            Assert.Equal("{\"palette\":[[1,2,3]],\"textColor\":\"palette\"}", App(m));
            Assert.Equal("{\"palette\":\"Rainbow\",\"textColor\":\"palette\"}", App(new AwtrixAppMessage().SetEffectPalette("Ocean").SetRainbow()));
        }

        [Fact]
        public void EffectPalette_Alone_DoesNotForceTextColorPalette()
        {
            Assert.Equal("{\"palette\":\"Ocean\"}", App(new AwtrixAppMessage().SetEffectPalette("Ocean")));
        }

        [Fact]
        public void BlinkAndFade_AreMilliseconds()
        {
            Assert.Equal("{\"textBlinkMs\":500,\"textFadeMs\":250}", App(new AwtrixAppMessage().SetBlinkText(500).SetFadeText(250)));
        }

        [Theory]
        [InlineData(0, "fixed")]
        [InlineData(1, "pushOnce")]
        [InlineData(2, "push")]
        public void PushIcon_ToIconMode(int value, string expected) => Assert.Equal($"{{\"iconMode\":\"{expected}\"}}", App(new AwtrixAppMessage().SetPushIcon(value)));

        [Fact]
        public void Icon_And_Duration()
        {
            Assert.Equal("{\"durationMs\":300000,\"icon\":\"1667\"}", App(new AwtrixAppMessage().SetIcon("1667").SetDuration(300)));
        }

        [Fact]
        public void Charts()
        {
            var m = new AwtrixAppMessage().SetLine(new[] { 1, 2 }).SetBar(new[] { 3, 4 }).SetAutoscale(true);
            Assert.Equal("{\"barChart\":[3,4],\"chartAutoscale\":true,\"lineChart\":[1,2]}", App(m));
        }

        [Fact]
        public void Progress()
        {
            var m = new AwtrixAppMessage().SetProgress(50).SetProgressC(new[] { 255, 0, 0 }).SetProgressBC(new[] { 0, 0, 255 });
            Assert.Equal("{\"progress\":50,\"progressColor\":[255,0,0],\"progressTrackColor\":[0,0,255]}", App(m));
        }

        [Fact]
        public void ScrollSpeed_BecomesScrollObject() => Assert.Equal("{\"scroll\":{\"speed\":80}}", App(new AwtrixAppMessage().SetScrollSpeed(80)));

        [Fact]
        public void Effects_And_Overlay()
        {
            var m = new AwtrixAppMessage().SetEffect("Matrix").SetEffectSpeed(3).SetEffectBlend(true).SetOverlay("rain");
            Assert.Equal("{\"effect\":\"Matrix\",\"effectSpeed\":3,\"overlay\":\"rain\",\"paletteBlend\":true}", App(m));
        }

        [Fact]
        public void UnknownKey_IsDroppedAndReported()
        {
            var m = new AwtrixAppMessage { ["bogus"] = "1" };
            var t = NgPayloadTranslator.Translate(m, NgPayloadKind.App);
            Assert.Equal("{}", t.Json);
            Assert.Equal(new[] { "bogus" }, t.DroppedKeys);
        }

        [Fact]
        public void UnparsableValue_IsDroppedAndReported()
        {
            var m = new AwtrixAppMessage { ["duration"] = "abc", ["color"] = "purple" };
            var t = NgPayloadTranslator.Translate(m, NgPayloadKind.App);
            Assert.Equal("{}", t.Json);
            Assert.Equal(new[] { "color", "duration" }, t.DroppedKeys.OrderBy(k => k).ToArray());
        }

        [Fact]
        public void TextIsFirst_ThenSortedKeys()
        {
            var m = new AwtrixAppMessage().SetProgress(1).SetIcon("1").SetText("x");
            Assert.Equal("{\"text\":\"x\",\"icon\":\"1\",\"progress\":1}", App(m));
        }

        [Fact]
        public void EmptyMessage_IsEmptyObject() => Assert.Equal("{}", App(new AwtrixAppMessage()));
    }
}
