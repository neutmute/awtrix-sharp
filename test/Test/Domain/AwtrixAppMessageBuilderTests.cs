using AwtrixSharpWeb.Domain;

namespace Test.Domain
{
    /// <summary>
    /// Covers the fluent setter surface of AwtrixAppMessage (a Dictionary&lt;string,string&gt;
    /// under the hood) - each Set* method should store the expected key/value and
    /// return "this" for chaining.
    /// </summary>
    public class AwtrixAppMessageBuilderTests
    {
        [Fact]
        public void SetText_SetsTextKeyAndProperty()
        {
            var message = new AwtrixAppMessage().SetText("Hello");

            Assert.Equal("Hello", message["text"]);
            Assert.Equal("Hello", message.Text);
        }

        [Fact]
        public void Text_WhenNotSet_ReturnsNull()
        {
            var message = new AwtrixAppMessage();

            Assert.Null(message.Text);
        }

        [Fact]
        public void SetTextCase_SetsIntegerAsString()
        {
            var message = new AwtrixAppMessage().SetTextCase(2);

            Assert.Equal("2", message["textCase"]);
        }

        [Theory]
        [InlineData(true, "true")]
        [InlineData(false, "false")]
        public void SetTopText_SetsLowercaseBoolean(bool value, string expected)
        {
            var message = new AwtrixAppMessage().SetTopText(value);

            Assert.Equal(expected, message["topText"]);
        }

        [Fact]
        public void SetHold_DefaultsToTrue()
        {
            var message = new AwtrixAppMessage().SetHold();

            Assert.Equal("true", message["hold"]);
        }

        [Fact]
        public void SetHold_ExplicitFalse()
        {
            var message = new AwtrixAppMessage().SetHold(false);

            Assert.Equal("false", message["hold"]);
        }

        [Fact]
        public void SetStack_DefaultsToTrue()
        {
            var message = new AwtrixAppMessage().SetStack();

            Assert.Equal("true", message["stack"]);
        }

        [Fact]
        public void SetTextOffset_SetsIntegerAsString()
        {
            var message = new AwtrixAppMessage().SetTextOffset(5);

            Assert.Equal("5", message["textOffset"]);
        }

        [Fact]
        public void SetCenter_SetsLowercaseBoolean()
        {
            var message = new AwtrixAppMessage().SetCenter(true);

            Assert.Equal("true", message["center"]);
        }

        [Fact]
        public void SetColor_SetsRawString()
        {
            var message = new AwtrixAppMessage().SetColor("#FF0000");

            Assert.Equal("#FF0000", message["color"]);
        }

        [Fact]
        public void SetGradient_JoinsNestedArraysWithSemicolonAndComma()
        {
            var message = new AwtrixAppMessage().SetGradient(new[] { new[] { 255, 0, 0 }, new[] { 0, 255, 0 } });

            Assert.Equal("255,0,0;0,255,0", message["gradient"]);
        }

        [Fact]
        public void SetGradient_WithNull_DoesNotAddKey()
        {
            var message = new AwtrixAppMessage().SetGradient(null!);

            Assert.False(message.ContainsKey("gradient"));
        }

        [Fact]
        public void SetGradient_WithEmptyArray_DoesNotAddKey()
        {
            var message = new AwtrixAppMessage().SetGradient(Array.Empty<int[]>());

            Assert.False(message.ContainsKey("gradient"));
        }

        [Fact]
        public void SetBlinkText_SetsDoubleAsString()
        {
            var message = new AwtrixAppMessage().SetBlinkText(0.5);

            Assert.Equal("0.5", message["blinkText"]);
        }

        [Fact]
        public void SetFadeText_SetsDoubleAsString()
        {
            var message = new AwtrixAppMessage().SetFadeText(1.5);

            Assert.Equal("1.5", message["fadeText"]);
        }

        [Fact]
        public void SetBackground_SetsRawString()
        {
            var message = new AwtrixAppMessage().SetBackground("#000000");

            Assert.Equal("#000000", message["background"]);
        }

        [Fact]
        public void SetRainbow_DefaultsToTrue()
        {
            var message = new AwtrixAppMessage().SetRainbow();

            Assert.Equal("true", message["rainbow"]);
        }

        [Fact]
        public void SetIcon_SetsRawString()
        {
            var message = new AwtrixAppMessage().SetIcon("2405");

            Assert.Equal("2405", message["icon"]);
        }

        [Fact]
        public void SetPushIcon_SetsIntegerAsString()
        {
            var message = new AwtrixAppMessage().SetPushIcon(1);

            Assert.Equal("1", message["pushIcon"]);
        }

        [Fact]
        public void SetDuration_FromSeconds_SetsIntegerSeconds()
        {
            var message = new AwtrixAppMessage().SetDuration(10);

            Assert.Equal("10", message["duration"]);
        }

        [Fact]
        public void SetDuration_FromTimeSpan_RoundsToWholeSeconds()
        {
            var message = new AwtrixAppMessage().SetDuration(TimeSpan.FromSeconds(7.4));

            Assert.Equal("7", message["duration"]);
        }

        [Fact]
        public void SetLine_JoinsWithComma()
        {
            var message = new AwtrixAppMessage().SetLine(new[] { 1, 2, 3 });

            Assert.Equal("1,2,3", message["line"]);
        }

        [Fact]
        public void SetLifetime_SetsIntegerAsString()
        {
            var message = new AwtrixAppMessage().SetLifetime(60);

            Assert.Equal("60", message["lifetime"]);
        }

        [Fact]
        public void SetLifetimeMode_SetsIntegerAsString()
        {
            var message = new AwtrixAppMessage().SetLifetimeMode(1);

            Assert.Equal("1", message["lifetimeMode"]);
        }

        [Fact]
        public void SetBar_JoinsWithComma()
        {
            var message = new AwtrixAppMessage().SetBar(new[] { 1, 2, 3, 4 });

            Assert.Equal("1,2,3,4", message["bar"]);
        }

        [Fact]
        public void SetAutoscale_SetsLowercaseBoolean()
        {
            var message = new AwtrixAppMessage().SetAutoscale(true);

            Assert.Equal("true", message["autoscale"]);
        }

        [Fact]
        public void SetOverlay_SetsRawString()
        {
            var message = new AwtrixAppMessage().SetOverlay("snow");

            Assert.Equal("snow", message["overlay"]);
        }

        [Fact]
        public void SetProgress_SetsIntegerAsString()
        {
            var message = new AwtrixAppMessage().SetProgress(75);

            Assert.Equal("75", message["progress"]);
        }

        [Fact]
        public void SetProgressC_JoinsWithComma()
        {
            var message = new AwtrixAppMessage().SetProgressC(new[] { 255, 0, 0 });

            Assert.Equal("255,0,0", message["progressC"]);
        }

        [Fact]
        public void SetProgressBC_JoinsWithComma()
        {
            var message = new AwtrixAppMessage().SetProgressBC(new[] { 0, 0, 255 });

            Assert.Equal("0,0,255", message["progressBC"]);
        }

        [Fact]
        public void SetScrollSpeed_SetsIntegerAsString()
        {
            var message = new AwtrixAppMessage().SetScrollSpeed(100);

            Assert.Equal("100", message["scrollSpeed"]);
        }

        [Fact]
        public void SetEffect_SetsRawString()
        {
            var message = new AwtrixAppMessage().SetEffect("Rain");

            Assert.Equal("Rain", message["effect"]);
        }

        [Fact]
        public void SetEffectSpeed_SetsIntegerAsString()
        {
            var message = new AwtrixAppMessage().SetEffectSpeed(50);

            Assert.Equal("50", message["effectSpeed"]);
        }

        [Fact]
        public void SetEffectPalette_SetsRawString()
        {
            var message = new AwtrixAppMessage().SetEffectPalette("Purple");

            Assert.Equal("Purple", message["effectPalette"]);
        }

        [Fact]
        public void SetEffectBlend_SetsLowercaseBoolean()
        {
            var message = new AwtrixAppMessage().SetEffectBlend(true);

            Assert.Equal("true", message["effectBlend"]);
        }

        [Fact]
        public void FluentChain_AllowsSettingMultiplePropertiesInSequence()
        {
            var message = new AwtrixAppMessage()
                .SetText("Hi")
                .SetColor("#FFFFFF")
                .SetDuration(5)
                .SetIcon("123")
                .SetProgress(10);

            Assert.Equal("Hi", message["text"]);
            Assert.Equal("#FFFFFF", message["color"]);
            Assert.Equal("5", message["duration"]);
            Assert.Equal("123", message["icon"]);
            Assert.Equal("10", message["progress"]);
        }

        [Fact]
        public void ToString_OmitsNothingAndJoinsWithSemicolons()
        {
            var message = new AwtrixAppMessage()
                .SetIcon("1")
                .SetColor("#FF0000");

            var result = message.ToString();

            Assert.Contains("icon=1", result);
            Assert.Contains("color=#FF0000", result);
            Assert.Contains("; ", result);
        }

        [Fact]
        public void ToString_ListsTextFirst()
        {
            var message = new AwtrixAppMessage()
                .SetIcon("1")
                .SetColor("#FF0000")
                .SetText("Hello");

            Assert.StartsWith("text=Hello; ", message.ToString());
        }

        [Fact]
        public void ToJson_EmptyMessage_ReturnsEmptyObject()
        {
            var message = new AwtrixAppMessage();

            Assert.Equal("{}", message.ToJson());
        }

        [Fact]
        public void ToJson_TextThatLooksLikeJsonButIsInvalid_FallsBackToRawString()
        {
            var message = new AwtrixAppMessage().SetText("[not valid json");

            var json = message.ToJson();

            Assert.Equal("{\"text\":\"[not valid json\"}", json);
        }
    }
}
