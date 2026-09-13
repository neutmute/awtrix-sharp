using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services;
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
        public bool ReturnValue { get; set; } = true;

        public RecordingPublisher() : base(NullLogger<RecordingPublisher>.Instance)
        {
        }

        public override Task<bool> Publish(string url, string payload)
        {
            LastUrl = url;
            LastPayload = payload;
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
    }
}
