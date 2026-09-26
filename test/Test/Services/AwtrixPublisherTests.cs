using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Test.Services
{
    /// <summary>
    /// Minimal concrete AwtrixPublisher used purely to exercise the abstract
    /// base class's Publish(url, payload) convenience.
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
        public async Task Publish_UrlAndPayload_IsAPostRequest()
        {
            var publisher = new RecordingPublisher();

            await publisher.Publish("topic/x", "{}");

            Assert.Equal(HttpMethod.Post, publisher.LastMethod);
            Assert.Equal("topic/x", publisher.LastUrl);
            Assert.Equal("{}", publisher.LastPayload);
        }

        [Fact]
        public void AwtrixRequest_Post_DefaultsMethod()
        {
            var request = AwtrixRequest.Post("a/b", "x");

            Assert.Equal(HttpMethod.Post, request.Method);
        }
    }
}
