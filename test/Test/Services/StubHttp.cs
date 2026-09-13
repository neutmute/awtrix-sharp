using System.Net;

namespace Test.Services
{
    /// <summary>
    /// HttpMessageHandler whose behaviour is supplied per test. Records requests and bodies.
    /// </summary>
    public sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> RequestBodies { get; } = new();

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public static StubHttpMessageHandler Returning(HttpStatusCode status)
        {
            return new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return await _handler(request, cancellationToken);
        }
    }

    /// <summary>
    /// IHttpClientFactory returning clients over a shared stub handler.
    /// </summary>
    public sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        private readonly TimeSpan _timeout;

        public List<string> RequestedNames { get; } = new();

        public StubHttpClientFactory(HttpMessageHandler handler, TimeSpan? timeout = null)
        {
            _handler = handler;
            _timeout = timeout ?? TimeSpan.FromSeconds(5);
        }

        public HttpClient CreateClient(string name)
        {
            RequestedNames.Add(name);
            return new HttpClient(_handler, disposeHandler: false) { Timeout = _timeout };
        }
    }
}
