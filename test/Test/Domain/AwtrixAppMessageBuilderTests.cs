using AwtrixSharpWeb.Domain;

namespace Test.Domain
{
    /// <summary>Every setter stores the NG key with a typed value (spec §1).</summary>
    public class AwtrixAppMessageBuilderTests
    {
        [Fact]
        public void SetText_SetsTextKeyAndProperty()
        {
            var m = new AwtrixAppMessage().SetText("hello");
            Assert.Equal("hello", m["text"]);
            Assert.Equal("hello", m.Text);
        }

        [Fact]
        public void Text_WhenNotSet_ReturnsNull() => Assert.Null(new AwtrixAppMessage().Text);

        [Fact]
        public void Text_WhenFragments_ReturnsNull()
        {
            var m = new AwtrixAppMessage().SetText(new[] { new TextFragment("a", "#FF0000") });
            Assert.Null(m.Text);
            Assert.IsType<TextFragment[]>(m["text"]);
        }

        [Theory]
        [InlineData(TextCase.Inherit)]
        [InlineData(TextCase.Upper)]
        [InlineData(TextCase.AsTyped)]
        public void SetTextCase_StoresEnum(TextCase value) => Assert.Equal(value, new AwtrixAppMessage().SetTextCase(value)["textCase"]);

        [Fact]
        public void SetHold_DefaultsToTrue() => Assert.Equal(true, new AwtrixAppMessage().SetHold()["hold"]);

        [Fact]
        public void SetHold_ExplicitFalse() => Assert.Equal(false, new AwtrixAppMessage().SetHold(false)["hold"]);

        [Fact]
        public void SetStack_DefaultsToTrue() => Assert.Equal(true, new AwtrixAppMessage().SetStack()["stack"]);

        [Fact]
        public void SetTextOffsetX_StoresInt() => Assert.Equal(-3, new AwtrixAppMessage().SetTextOffsetX(-3)["textOffsetX"]);

        [Fact]
        public void SetTextCenter_StoresBool() => Assert.Equal(false, new AwtrixAppMessage().SetTextCenter(false)["textCenter"]);

        [Fact]
        public void SetTextColor_HexIsStored() => Assert.Equal("#FF0000", new AwtrixAppMessage().SetTextColor("#FF0000")["textColor"]);

        [Fact]
        public void SetTextColor_Palette_IsStoredLiterally() => Assert.Equal("palette", new AwtrixAppMessage().SetTextColor("palette")["textColor"]);

        [Fact]
        public void SetTextColor_Rgb_IsStoredAsArray() => Assert.Equal(new[] { 1, 2, 3 }, new AwtrixAppMessage().SetTextColor("1,2,3")["textColor"]);

        [Fact]
        public void SetTextColor_BareHex_Throws() => Assert.Throws<ArgumentException>(() => new AwtrixAppMessage().SetTextColor("FF0000"));

        [Fact]
        public void SetBackgroundColor_Stores() => Assert.Equal("#000", new AwtrixAppMessage().SetBackgroundColor("#000")["backgroundColor"]);

        [Fact]
        public void SetPalette_Name() => Assert.Equal("Rainbow", new AwtrixAppMessage().SetPalette("Rainbow")["palette"]);

        [Fact]
        public void SetPalette_Matrix()
        {
            var matrix = new[] { new[] { 255, 0, 0 }, new[] { 0, 255, 0 } };
            Assert.Same(matrix, new AwtrixAppMessage().SetPalette(matrix)["palette"]);
        }

        [Fact]
        public void SetPalette_EmptyMatrix_DoesNotAddKey() => Assert.False(new AwtrixAppMessage().SetPalette(Array.Empty<int[]>()).ContainsKey("palette"));

        [Fact]
        public void SetPaletteBlend_StoresBool() => Assert.Equal(true, new AwtrixAppMessage().SetPaletteBlend(true)["paletteBlend"]);

        [Fact]
        public void SetTextBlinkMs_And_SetTextFadeMs_StoreInts()
        {
            var m = new AwtrixAppMessage().SetTextBlinkMs(500).SetTextFadeMs(1500);
            Assert.Equal(500, m["textBlinkMs"]);
            Assert.Equal(1500, m["textFadeMs"]);
        }

        [Fact]
        public void SetIcon_StoresString() => Assert.Equal("1667", new AwtrixAppMessage().SetIcon("1667")["icon"]);

        [Fact]
        public void SetIconMode_StoresEnum() => Assert.Equal(IconMode.PushOnce, new AwtrixAppMessage().SetIconMode(IconMode.PushOnce)["iconMode"]);

        [Fact]
        public void SetDurationMs_StoresInt() => Assert.Equal(5000, new AwtrixAppMessage().SetDurationMs(5000)["durationMs"]);

        [Fact]
        public void SetDuration_TimeSpan_RoundsToWholeMs() => Assert.Equal(7500, new AwtrixAppMessage().SetDuration(TimeSpan.FromMilliseconds(7500.4))["durationMs"]);

        [Fact]
        public void SetLifetimeMs_And_SetLifetime_StoreInts()
        {
            Assert.Equal(60000, new AwtrixAppMessage().SetLifetimeMs(60000)["lifetimeMs"]);
            Assert.Equal(60000, new AwtrixAppMessage().SetLifetime(TimeSpan.FromMinutes(1))["lifetimeMs"]);
        }

        [Fact]
        public void SetLifetimeExpiry_StoresEnum() => Assert.Equal(LifetimeExpiry.Mark, new AwtrixAppMessage().SetLifetimeExpiry(LifetimeExpiry.Mark)["lifetimeExpiry"]);

        [Fact]
        public void SetLineChart_And_SetBarChart_StoreArrays()
        {
            var m = new AwtrixAppMessage().SetLineChart(new[] { 1, 2 }).SetBarChart(new[] { 3 });
            Assert.Equal(new[] { 1, 2 }, m["lineChart"]);
            Assert.Equal(new[] { 3 }, m["barChart"]);
        }

        [Fact]
        public void SetChartAutoscale_StoresBool() => Assert.Equal(true, new AwtrixAppMessage().SetChartAutoscale(true)["chartAutoscale"]);

        [Fact]
        public void SetOverlay_StoresString() => Assert.Equal("snow", new AwtrixAppMessage().SetOverlay("snow")["overlay"]);

        [Fact]
        public void SetProgress_StoresInt() => Assert.Equal(75, new AwtrixAppMessage().SetProgress(75)["progress"]);

        [Fact]
        public void SetProgressColor_And_TrackColor_StoreColours()
        {
            var m = new AwtrixAppMessage().SetProgressColor("255,0,0").SetProgressTrackColor("#0000FF");
            Assert.Equal(new[] { 255, 0, 0 }, m["progressColor"]);
            Assert.Equal("#0000FF", m["progressTrackColor"]);
        }

        [Fact]
        public void SetScrollSpeed_StoresNestedObject()
        {
            var scroll = Assert.IsType<Dictionary<string, object>>(new AwtrixAppMessage().SetScrollSpeed(100)["scroll"]);
            Assert.Equal(100, scroll["speed"]);
        }

        [Fact]
        public void SetEffect_StoresString() => Assert.Equal("Matrix", new AwtrixAppMessage().SetEffect("Matrix")["effect"]);

        [Fact]
        public void SetEffectSpeed_StoresDouble() => Assert.Equal(1.5, new AwtrixAppMessage().SetEffectSpeed(1.5)["effectSpeed"]);

        [Fact]
        public void FluentChain_AllowsSettingMultiplePropertiesInSequence()
        {
            var m = new AwtrixAppMessage().SetText("t").SetTextColor("#FF0000").SetDurationMs(5000).SetProgress(10);
            Assert.Equal(4, m.Count);
        }

        [Fact]
        public void ToString_ListsTextFirst_ThenSortedKeys_WithJsonForNonStrings()
        {
            var m = new AwtrixAppMessage().SetProgress(10).SetTextColor("1,2,3").SetText("t").SetHold();
            Assert.Equal("text=t; hold=true; progress=10; textColor=[1,2,3]", m.ToString());
        }
    }
}
