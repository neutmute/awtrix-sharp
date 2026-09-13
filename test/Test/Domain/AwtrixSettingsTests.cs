using AwtrixSharpWeb.Domain;

namespace Test.Domain
{
    public class AwtrixSettingsTests
    {
        [Fact]
        public void SetGlobalTextColor_SetsTcolKey()
        {
            var settings = new AwtrixSettings().SetGlobalTextColor("#00FF00");

            Assert.Equal("#00FF00", settings["TCOL"]);
        }

        [Fact]
        public void SetBrightness_SetsBriKeyAsString()
        {
            var settings = new AwtrixSettings().SetBrightness(200);

            Assert.Equal("200", settings["BRI"]);
        }

        [Fact]
        public void FluentChain_AllowsSettingMultipleValues()
        {
            var settings = new AwtrixSettings()
                .SetGlobalTextColor("#FFFFFF")
                .SetBrightness(50);

            Assert.Equal("#FFFFFF", settings["TCOL"]);
            Assert.Equal("50", settings["BRI"]);
        }

        [Fact]
        public void ToJson_SerializesAllKeys()
        {
            var settings = new AwtrixSettings()
                .SetGlobalTextColor("#FFFFFF")
                .SetBrightness(50);

            var json = settings.ToJson();

            Assert.Equal("{\"TCOL\":\"#FFFFFF\",\"BRI\":\"50\"}", json);
        }

        [Fact]
        public void ToJson_EmptySettings_ReturnsEmptyObject()
        {
            var settings = new AwtrixSettings();

            Assert.Equal("{}", settings.ToJson());
        }

        [Fact]
        public void ToString_JoinsKeyValuePairsWithSemicolon()
        {
            var settings = new AwtrixSettings()
                .SetGlobalTextColor("#FFFFFF")
                .SetBrightness(50);

            var result = settings.ToString();

            Assert.Equal("TCOL=#FFFFFF;BRI=50", result);
        }

        [Fact]
        public void ToString_WhenEmpty_ReturnsEmptyString()
        {
            // Previously threw InvalidOperationException (Aggregate on empty) inside a tick handler (CR-01).
            var settings = new AwtrixSettings();

            Assert.Equal(string.Empty, settings.ToString());
        }
    }
}
