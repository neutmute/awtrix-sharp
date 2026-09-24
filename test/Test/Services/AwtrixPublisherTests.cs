using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Services.Firmware;
using Microsoft.Extensions.Logging.Abstractions;

namespace Test.Services
{
    /// <summary>
    /// Minimal concrete AwtrixPublisher used purely to exercise the abstract
    /// base class's ToJson/Publish(message) plumbing.
    /// </summary>
    public class RecordingPublisher : AwtrixPublisher
    {
        public string? LastUrl { get; private set; }
        public string? LastPayload { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public bool ReturnValue { get; set; } = true;

        public RecordingPublisher() : base(NullLogger<RecordingPublisher>.Instance)
        {
        }

        public override Task<bool> Publish(AwtrixRequest request)
        {
            LastUrl = request.Address;
            LastPayload = request.Payload;
            LastMethod = request.Method;
            return Task.FromResult(ReturnValue);
        }
    }

    public class AwtrixPublisherTests
    {
        [Fact]
        public void ToJson_NullMessage_ReturnsEmptyString()
        {
            var publisher = new RecordingPublisher();

            var json = publisher.ToJson(null);

            Assert.Equal(string.Empty, json);
        }

        [Fact]
        public void ToJson_WithMessage_DelegatesToMessageToJson()
        {
            var publisher = new RecordingPublisher();
            var message = new AwtrixAppMessage().SetText("hi");

            var json = publisher.ToJson(message);

            Assert.Equal(message.ToJson(), json);
        }

        [Fact]
        public async Task Publish_WithNullMessage_PublishesEmptyStringPayload()
        {
            var publisher = new RecordingPublisher();

            var result = await publisher.Publish("topic/x", (AwtrixAppMessage?)null);

            Assert.True(result);
            Assert.Equal("topic/x", publisher.LastUrl);
            Assert.Equal(string.Empty, publisher.LastPayload);
        }

        [Fact]
        public async Task Publish_WithMessage_PublishesSerializedJson()
        {
            var publisher = new RecordingPublisher();
            var message = new AwtrixAppMessage().SetText("hi").SetProgress(50);

            var result = await publisher.Publish("topic/x", message);

            Assert.True(result);
            Assert.Equal(message.ToJson(), publisher.LastPayload);
        }

        [Fact]
        public async Task Publish_ReturnsUnderlyingImplementationResult()
        {
            var publisher = new RecordingPublisher { ReturnValue = false };

            var result = await publisher.Publish("topic/x", new AwtrixAppMessage().SetText("hi"));

            Assert.False(result);
        }

        [Fact]
        public void BuildCustomAppUrl_Default_UsesMqttTopicForm()
        {
            var publisher = new RecordingPublisher();

            Assert.Equal("awtrix/clock1/custom/MyApp", publisher.BuildCustomAppUrl("awtrix/clock1", "MyApp"));
        }

        [Fact]
        public async Task Publish_UrlAndPayload_IsAPostRequest()
        {
            var publisher = new RecordingPublisher();

            await publisher.Publish("topic/x", "{}");

            Assert.Equal(HttpMethod.Post, publisher.LastMethod);
            Assert.Equal("topic/x", publisher.LastUrl);
            Assert.Equal("{}", publisher.LastPayload);
        }

        [Fact]
        public void AwtrixRequest_Post_DefaultsMethodAndEmptyDroppedKeys()
        {
            var request = AwtrixRequest.Post("a/b", "x");

            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Empty(request.DroppedKeys);
        }
    }
}
