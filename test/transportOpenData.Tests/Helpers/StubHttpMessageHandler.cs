using System.Net;
using System.Text;

namespace TransportOpenData.Tests.Helpers
{
    /// <summary>
    /// Answers every request from a delegate so generated clients run end to end without a network.
    /// </summary>
    public sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        public List<Uri> RequestUris { get; } = new();

        public static StubHttpMessageHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new((_, _) => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (RequestUris)
            {
                RequestUris.Add(request.RequestUri!);
            }

            return _respond(request, cancellationToken);
        }
    }
}
