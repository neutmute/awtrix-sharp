using AwtrixSharpWeb.Domain;

namespace Test.Domain
{
    public class AwtrixSettingsTests
    {
        [Fact]
        public void SetTextColor_StoresParsedColour()
        {
            Assert.Equal("#FFFFFF", new AwtrixSettings().SetTextColor("#FFFFFF")["textColor"]);
            Assert.Equal(new[] { 1, 2, 3 }, new AwtrixSettings().SetTextColor("1,2,3")["textColor"]);
        }

        [Fact]
        public void SetTextColor_BareHex_Throws() => Assert.Throws<ArgumentException>(() => new AwtrixSettings().SetTextColor("FFFFFF"));

        [Fact]
        public void SetBrightness_StoresInt() => Assert.Equal(200, new AwtrixSettings().SetBrightness(200)["brightness"]);

        [Fact]
        public void ToJson_SortsKeys()
        {
            Assert.Equal("{\"brightness\":50,\"textColor\":\"#FFFFFF\"}", new AwtrixSettings().SetTextColor("#FFFFFF").SetBrightness(50).ToJson());
        }

        [Fact]
        public void ToJson_EmptySettings_ReturnsEmptyObject() => Assert.Equal("{}", new AwtrixSettings().ToJson());

        [Fact]
        public void ToString_JoinsKeyValuePairsWithSemicolon()
        {
            Assert.Equal("brightness=50;textColor=#FFFFFF", new AwtrixSettings().SetTextColor("#FFFFFF").SetBrightness(50).ToString());
        }

        [Fact]
        public void ToString_WhenEmpty_ReturnsEmptyString() => Assert.Equal("", new AwtrixSettings().ToString());
    }
}
