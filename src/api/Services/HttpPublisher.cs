using System.Text;

namespace AwtrixSharpWeb.Services
{
    /// <summary>
    /// Publishes to an Awtrix device's HTTP API. Never throws: every failure is logged and reported as false.
    /// </summary>
    public class HttpPublisher : AwtrixPublisher
    {
        public const string HttpClientName = "AwtrixHttpPublisher";

        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

        private readonly IHttpClientFactory _httpClientFactory;

        public HttpPublisher(ILogger<HttpPublisher> logger, IHttpClientFactory httpClientFactory) : base(logger)
        {
            _httpClientFactory = httpClientFactory;
        }

        private const int MaxLoggedBodyLength = 512;

        public override async Task<bool> Publish(AwtrixRequest request)
        {
            var url = request.Address;
            try
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                using var httpRequest = new HttpRequestMessage(request.Method, url);
                // DELETE carries no body; every other verb sends JSON, even when empty
                if (request.Method != HttpMethod.Delete || request.Payload.Length > 0)
                {
                    httpRequest.Content = new StringContent(request.Payload, Encoding.UTF8, "application/json");
                }
                using var response = await client.SendAsync(httpRequest);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                var body = await ReadBodyForLog(response);
                Logger.LogWarning("HTTP {Method} to {Url} returned {StatusCode}{Body}", request.Method, url, (int)response.StatusCode, body);
                return false;
            }
            catch (Exception ex)
            {
                // Routine when a device is offline: log type + message, not the stack trace
                Logger.LogWarning("HTTP publish to {Url} failed: {ErrorType}: {Error}", url, ex.GetType().Name, ex.Message);
                return false;
            }
        }

        /// <summary>NG answers 4xx with a JSON body naming the offending field; surface it, truncated.</summary>
        private static async Task<string> ReadBodyForLog(HttpResponseMessage response)
        {
            try
            {
                var body = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(body))
                {
                    return string.Empty;
                }
                return ": " + (body.Length > MaxLoggedBodyLength ? body.Substring(0, MaxLoggedBodyLength) + "…" : body);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
