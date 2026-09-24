using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services.Firmware;

namespace Test.Services.Firmware
{
    /// <summary>Pins the AWTRIX 3 dialect exactly as the service sent it before the firmware seam existed.</summary>
    public class Awtrix3FirmwareTests
    {
        private static readonly IAwtrixFirmware Sut = AwtrixFirmware.For(AwtrixFirmwareKind.Awtrix3);
        private static readonly AwtrixAddress Mqtt = new() { BaseTopic = "awtrix/clock1" };
        private static readonly AwtrixAddress Http = new() { BaseTopic = "http://192.168.1.50/api" };

        [Fact]
        public void Kind_IsAwtrix3() => Assert.Equal(AwtrixFirmwareKind.Awtrix3, Sut.Kind);

        [Fact]
        public void AppUpdate_Mqtt()
        {
            var r = Sut.AppUpdate(Mqtt, "TripTimerApp", new AwtrixAppMessage().SetText("42"));
            Assert.Equal("awtrix/clock1/custom/TripTimerApp", r.Address);
            Assert.Equal(HttpMethod.Post, r.Method);
            Assert.Equal("{\"text\":\"42\"}", r.Payload);
            Assert.Empty(r.DroppedKeys);
        }

        [Fact]
        public void AppUpdate_Http_EscapesName()
        {
            var r = Sut.AppUpdate(Http, "My App", new AwtrixAppMessage().SetText("42"));
            Assert.Equal("http://192.168.1.50/api/custom?name=My%20App", r.Address);
            Assert.Equal(HttpMethod.Post, r.Method);
        }

        [Fact]
        public void AppClear_IsEmptyPostToSameAddress()
        {
            Assert.Equal(("awtrix/clock1/custom/X", HttpMethod.Post, ""), Deconstruct(Sut.AppClear(Mqtt, "X")));
            Assert.Equal(("http://192.168.1.50/api/custom?name=X", HttpMethod.Post, ""), Deconstruct(Sut.AppClear(Http, "X")));
        }

        [Fact]
        public void Notify_And_Dismiss()
        {
            Assert.Equal(("awtrix/clock1/notify", HttpMethod.Post, "{\"text\":\"hi\"}"), Deconstruct(Sut.Notify(Mqtt, new AwtrixAppMessage().SetText("hi"))));
            Assert.Equal(("awtrix/clock1/notify/dismiss", HttpMethod.Post, ""), Deconstruct(Sut.Dismiss(Mqtt)));
            Assert.Equal(("http://192.168.1.50/api/notify/dismiss", HttpMethod.Post, ""), Deconstruct(Sut.Dismiss(Http)));
        }

        [Fact]
        public void Settings_SerialisesRawKeys()
        {
            var r = Sut.Settings(Mqtt, new AwtrixSettings().SetBrightness(8).SetGlobalTextColor("#FFFFFF"));
            Assert.Equal("awtrix/clock1/settings", r.Address);
            Assert.Equal("{\"BRI\":\"8\",\"TCOL\":\"#FFFFFF\"}", r.Payload);
        }

        [Fact]
        public void PlayRtttl_IsRawString()
        {
            Assert.Equal(("awtrix/clock1/rtttl", HttpMethod.Post, "a:d=4:c"), Deconstruct(Sut.PlayRtttl(Mqtt, "a:d=4:c")));
        }

        [Theory]
        [InlineData(Button.Left, "awtrix/clock1/stats/buttonLeft")]
        [InlineData(Button.Select, "awtrix/clock1/stats/buttonSelect")]
        [InlineData(Button.Right, "awtrix/clock1/stats/buttonRight")]
        public void ButtonTopic(Button button, string expected) => Assert.Equal(expected, Sut.ButtonTopic(Mqtt, button));

        [Fact]
        public void For_ReturnsSameInstance() => Assert.Same(Sut, AwtrixFirmware.For(AwtrixFirmwareKind.Awtrix3));

        private static (string, HttpMethod, string) Deconstruct(AwtrixRequest r) => (r.Address, r.Method, r.Payload);
    }
}
