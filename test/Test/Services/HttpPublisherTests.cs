using System.Net;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Test.Services
{
    public class HttpPublisherTests
    {
        private static HttpPublisher CreatePublisher(StubHttpMessageHandler handler, out StubHttpClientFactory factory, TimeSpan? timeout = null)
        {
            factory = new StubHttpClientFactory(handler, timeout);
            return new HttpPublisher(NullLogger<HttpPublisher>.Instance, factory);
        }

        [Fact]
        public async Task Publish_Success_ReturnsTrue_AndPostsJsonToUrlUsingNamedClient()
        {
            var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK);
            var publisher = CreatePublisher(handler, out var factory);

            var result = await publisher.Publish("http://192.168.1.50/api/notify", "{\"text\":\"hi\"}");

            Assert.True(result);
            var request = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://192.168.1.50/api/notify", request.RequestUri!.ToString());
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            Assert.Equal("{\"text\":\"hi\"}", handler.RequestBodies[0]);
            Assert.Equal(new[] { HttpPublisher.HttpClientName }, factory.RequestedNames);
        }

        [Fact]
        public async Task Publish_NonSuccessStatus_ReturnsFalse()
        {
            var publisher = CreatePublisher(StubHttpMessageHandler.Returning(HttpStatusCode.NotFound), out _);

            var result = await publisher.Publish("http://192.168.1.50/api/custom/TripTimerApp", "{}");

            Assert.False(result);
        }

        [Fact]
        public async Task Publish_WhenConnectionFails_ReturnsFalseWithoutThrowing()
        {
            var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("No route to host"));
            var publisher = CreatePublisher(handler, out _);

            var result = await publisher.Publish("http://192.168.1.50/api/notify", "{}");

            Assert.False(result);
        }

        [Fact]
        public async Task Publish_WhenRequestTimesOut_ReturnsFalseWithoutThrowing()
        {
            var handler = new StubHttpMessageHandler(async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            var publisher = CreatePublisher(handler, out _, TimeSpan.FromMilliseconds(100));

            var result = await publisher.Publish("http://192.168.1.50/api/notify", "{}");

            Assert.False(result);
        }

        [Fact]
        public async Task Publish_WithRelativeUrl_ReturnsFalseWithoutThrowing()
        {
            var publisher = CreatePublisher(StubHttpMessageHandler.Returning(HttpStatusCode.OK), out _);

            var result = await publisher.Publish("awtrix/clock1/notify", "{}");

            Assert.False(result);
        }

        [Fact]
        public async Task Publish_DisposesResponse()
        {
            var content = new TrackingContent();
            var handler = new StubHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
            var publisher = CreatePublisher(handler, out _);

            await publisher.Publish("http://192.168.1.50/api/notify", "{}");

            Assert.True(content.Disposed);
        }

        [Fact]
        public void DefaultTimeout_IsFiveSeconds()
        {
            Assert.Equal(TimeSpan.FromSeconds(5), HttpPublisher.DefaultTimeout);
        }

        private sealed class TrackingContent : StringContent
        {
            public bool Disposed { get; private set; }

            public TrackingContent() : base(string.Empty)
            {
            }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
