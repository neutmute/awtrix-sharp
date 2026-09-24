using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services.Firmware;

namespace Test.Services.Firmware
{
    public class NgSettingsTranslatorTests
    {
        [Fact]
        public void Brightness_And_TextColor()
        {
            var t = NgSettingsTranslator.Translate(new AwtrixSettings().SetBrightness(8).SetGlobalTextColor("FFFFFF"));
            Assert.Equal("{\"brightness\":8,\"textColor\":\"#FFFFFF\"}", t.Json);
            Assert.Empty(t.DroppedKeys);
        }

        [Fact]
        public void UnknownKey_IsDroppedAndReported()
        {
            var t = NgSettingsTranslator.Translate(new AwtrixSettings { ["ABRI"] = "true" });
            Assert.Equal("{}", t.Json);
            Assert.Equal(new[] { "ABRI" }, t.DroppedKeys);
        }

        [Fact]
        public void UnparsableValue_IsDroppedAndReported()
        {
            var t = NgSettingsTranslator.Translate(new AwtrixSettings { ["BRI"] = "bright", ["TCOL"] = "white" });
            Assert.Equal("{}", t.Json);
            Assert.Equal(new[] { "BRI", "TCOL" }, t.DroppedKeys.OrderBy(k => k).ToArray());
        }
    }
}
