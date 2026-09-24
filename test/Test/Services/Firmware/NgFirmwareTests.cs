using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services.Firmware;

namespace Test.Services.Firmware
{
    /// <summary>Pins the NG addressing table from the spec (reference/mqtt, reference/http).</summary>
    public class NgFirmwareTests
    {
        private static readonly IAwtrixFirmware Sut = AwtrixFirmware.For(AwtrixFirmwareKind.NG);
        private static readonly AwtrixAddress Mqtt = new() { BaseTopic = "awtrix/clock2", Firmware = AwtrixFirmwareKind.NG };
        private static readonly AwtrixAddress Http = new() { BaseTopic = "http://localhost:8080", Firmware = AwtrixFirmwareKind.NG };

        [Fact]
        public void Kind_IsNG() => Assert.Equal(AwtrixFirmwareKind.NG, Sut.Kind);

        [Fact]
        public void AppUpdate_Mqtt()
        {
            var r = Sut.AppUpdate(Mqtt, "TripTimerApp", new AwtrixAppMessage().SetText("42").SetDuration(5));
            Assert.Equal("awtrix/clock2/cmd/apps/pushed/TripTimerApp", r.Address);
            Assert.Equal("{\"text\":\"42\",\"durationMs\":5000}", r.Payload);
        }

        [Fact]
        public void AppUpdate_Http_IsPutWithEscapedName()
        {
            var r = Sut.AppUpdate(Http, "My App", new AwtrixAppMessage().SetText("42"));
            Assert.Equal("http://localhost:8080/api/v1/apps/pushed/My%20App", r.Address);
            Assert.Equal(HttpMethod.Put, r.Method);
        }

        [Fact]
        public void AppUpdate_ReportsDroppedKeys()
        {
            var r = Sut.AppUpdate(Mqtt, "X", new AwtrixAppMessage().SetTopText(true).SetText("x"));
            Assert.Equal(new[] { "topText" }, r.DroppedKeys);
        }

        [Fact]
        public void AppClear_Mqtt_IsEmptyPayload()
        {
            var r = Sut.AppClear(Mqtt, "X");
            Assert.Equal("awtrix/clock2/cmd/apps/pushed/X", r.Address);
            Assert.Equal(string.Empty, r.Payload);
        }

        [Fact]
        public void AppClear_Http_IsDelete()
        {
            var r = Sut.AppClear(Http, "X");
            Assert.Equal("http://localhost:8080/api/v1/apps/X", r.Address);
            Assert.Equal(HttpMethod.Delete, r.Method);
            Assert.Equal(string.Empty, r.Payload);
        }

        [Fact]
        public void Notify_UsesNotificationKind()
        {
            var m = new AwtrixAppMessage().SetText("hi").SetHold().SetLifetime(5);
            var mqtt = Sut.Notify(Mqtt, m);
            Assert.Equal("awtrix/clock2/cmd/notify", mqtt.Address);
            Assert.Equal("{\"text\":\"hi\",\"hold\":true}", mqtt.Payload);
            var http = Sut.Notify(Http, m);
            Assert.Equal("http://localhost:8080/api/v1/notifications", http.Address);
            Assert.Equal(HttpMethod.Post, http.Method);
        }

        [Fact]
        public void Dismiss()
        {
            Assert.Equal(("awtrix/clock2/cmd/notify/dismiss", HttpMethod.Post, ""), D(Sut.Dismiss(Mqtt)));
            Assert.Equal(("http://localhost:8080/api/v1/notifications/active", HttpMethod.Delete, ""), D(Sut.Dismiss(Http)));
        }

        [Fact]
        public void Settings()
        {
            var s = new AwtrixSettings().SetBrightness(8);
            Assert.Equal(("awtrix/clock2/cmd/settings", HttpMethod.Post, "{\"brightness\":8}"), D(Sut.Settings(Mqtt, s)));
            Assert.Equal(("http://localhost:8080/api/v1/settings", HttpMethod.Patch, "{\"brightness\":8}"), D(Sut.Settings(Http, s)));
            Assert.Equal(new[] { "ABRI" }, Sut.Settings(Mqtt, new AwtrixSettings { ["ABRI"] = "1" }).DroppedKeys);
        }

        [Fact]
        public void PlayRtttl_WrapsInJson()
        {
            Assert.Equal(("awtrix/clock2/cmd/audio/play", HttpMethod.Post, "{\"rtttl\":\"a:d=4:c\"}"), D(Sut.PlayRtttl(Mqtt, "a:d=4:c")));
            Assert.Equal(("http://localhost:8080/api/v1/audio/play", HttpMethod.Post, "{\"rtttl\":\"a:d=4:c\"}"), D(Sut.PlayRtttl(Http, "a:d=4:c")));
        }

        [Theory]
        [InlineData(Button.Left, "awtrix/clock2/state/buttons/left")]
        [InlineData(Button.Select, "awtrix/clock2/state/buttons/select")]
        [InlineData(Button.Right, "awtrix/clock2/state/buttons/right")]
        public void ButtonTopic(Button button, string expected) => Assert.Equal(expected, Sut.ButtonTopic(Mqtt, button));

        [Theory]
        [InlineData("http://host", "http://host")]
        [InlineData("http://host/", "http://host")]
        [InlineData("http://host/api", "http://host")]
        [InlineData("http://host/api/", "http://host")]
        [InlineData("http://host/api/v1", "http://host")]
        [InlineData("http://host:8080/api/v1/", "http://host:8080")]
        [InlineData("HTTP://Host/API", "HTTP://Host")]
        public void HttpRoot_StripsApiSuffixes(string input, string expected) => Assert.Equal(expected, NgFirmware.HttpRoot(input));

        [Fact]
        public void For_ReturnsSameInstance() => Assert.Same(Sut, AwtrixFirmware.For(AwtrixFirmwareKind.NG));

        private static (string, HttpMethod, string) D(AwtrixRequest r) => (r.Address, r.Method, r.Payload);
    }
}
