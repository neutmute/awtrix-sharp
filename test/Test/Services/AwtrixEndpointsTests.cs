using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services;

namespace Test.Services
{
    /// <summary>Pins the NG addressing table (spec §2).</summary>
    public class AwtrixEndpointsTests
    {
        private static readonly AwtrixAddress Mqtt = new() { BaseTopic = "awtrix/clock2" };
        private static readonly AwtrixAddress Http = new() { BaseTopic = "http://localhost:8080" };

        [Fact]
        public void AppUpdate_Mqtt_IsPostWithBody()
        {
            Assert.Equal(("awtrix/clock2/cmd/apps/pushed/TripTimerApp", HttpMethod.Post, "{\"text\":\"42\"}"),
                D(AwtrixEndpoints.AppUpdate(Mqtt, "TripTimerApp", "{\"text\":\"42\"}")));
        }

        [Fact]
        public void AppUpdate_Http_IsPutWithEscapedName()
        {
            Assert.Equal(("http://localhost:8080/api/v1/apps/pushed/My%20App", HttpMethod.Put, "{}"),
                D(AwtrixEndpoints.AppUpdate(Http, "My App", "{}")));
        }

        [Fact]
        public void AppClear_Mqtt_IsEmptyPayload()
        {
            Assert.Equal(("awtrix/clock2/cmd/apps/pushed/X", HttpMethod.Post, ""), D(AwtrixEndpoints.AppClear(Mqtt, "X")));
        }

        [Fact]
        public void AppClear_Http_IsDelete()
        {
            Assert.Equal(("http://localhost:8080/api/v1/apps/My%20App", HttpMethod.Delete, ""), D(AwtrixEndpoints.AppClear(Http, "My App")));
        }

        [Fact]
        public void Notify()
        {
            Assert.Equal(("awtrix/clock2/cmd/notify", HttpMethod.Post, "{\"text\":\"hi\"}"), D(AwtrixEndpoints.Notify(Mqtt, "{\"text\":\"hi\"}")));
            Assert.Equal(("http://localhost:8080/api/v1/notifications", HttpMethod.Post, "{\"text\":\"hi\"}"), D(AwtrixEndpoints.Notify(Http, "{\"text\":\"hi\"}")));
        }

        [Fact]
        public void Dismiss()
        {
            Assert.Equal(("awtrix/clock2/cmd/notify/dismiss", HttpMethod.Post, ""), D(AwtrixEndpoints.Dismiss(Mqtt)));
            Assert.Equal(("http://localhost:8080/api/v1/notifications/active", HttpMethod.Delete, ""), D(AwtrixEndpoints.Dismiss(Http)));
        }

        [Fact]
        public void Settings()
        {
            Assert.Equal(("awtrix/clock2/cmd/settings", HttpMethod.Post, "{\"brightness\":8}"), D(AwtrixEndpoints.Settings(Mqtt, "{\"brightness\":8}")));
            Assert.Equal(("http://localhost:8080/api/v1/settings", HttpMethod.Patch, "{\"brightness\":8}"), D(AwtrixEndpoints.Settings(Http, "{\"brightness\":8}")));
        }

        [Fact]
        public void PlayRtttl_WrapsInJson()
        {
            Assert.Equal(("awtrix/clock2/cmd/audio/play", HttpMethod.Post, "{\"rtttl\":\"a:d=4:c\"}"), D(AwtrixEndpoints.PlayRtttl(Mqtt, "a:d=4:c")));
            Assert.Equal(("http://localhost:8080/api/v1/audio/play", HttpMethod.Post, "{\"rtttl\":\"a:d=4:c\"}"), D(AwtrixEndpoints.PlayRtttl(Http, "a:d=4:c")));
        }

        [Theory]
        [InlineData(Button.Left, "awtrix/clock2/state/buttons/left")]
        [InlineData(Button.Select, "awtrix/clock2/state/buttons/select")]
        [InlineData(Button.Right, "awtrix/clock2/state/buttons/right")]
        public void ButtonTopic(Button button, string expected) => Assert.Equal(expected, AwtrixEndpoints.ButtonTopic(Mqtt, button));

        [Theory]
        [InlineData("http://host", "http://host")]
        [InlineData("http://host/", "http://host")]
        [InlineData("http://host:8080//", "http://host:8080")]
        public void HttpRoot_TrimsTrailingSlashes(string input, string expected) => Assert.Equal(expected, AwtrixEndpoints.HttpRoot(input));

        [Theory]
        [InlineData("http://host/api", "http://host/api/api/v1/notifications")]
        [InlineData("http://host/api/v1/", "http://host/api/v1/api/v1/notifications")]
        public void HttpRoot_DoesNotStripApiSuffix(string baseTopic, string expectedAddress)
        {
            // Review Focus 3: a stale AWTRIX 3 address must fail visibly, not be silently repaired
            var r = AwtrixEndpoints.Notify(new AwtrixAddress { BaseTopic = baseTopic }, "{}");
            Assert.Equal(expectedAddress, r.Address);
        }

        private static (string, HttpMethod, string) D(AwtrixRequest r) => (r.Address, r.Method, r.Payload);
    }
}
