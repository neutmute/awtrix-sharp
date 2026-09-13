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

        /// <summary>
        /// Awtrix 3 HTTP API: POST http://[ip]/api/custom?name=[app]
        /// </summary>
        public override string BuildCustomAppUrl(string baseTopic, string appName)
        {
            return $"{baseTopic.TrimEnd('/')}/custom?name={Uri.EscapeDataString(appName)}";
        }

        public override async Task<bool> Publish(string url, string payload)
        {
            try
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                using var content = new StringContent(payload ?? string.Empty, Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                Logger.LogWarning("HTTP publish to {Url} returned {StatusCode}", url, (int)response.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                // Routine when a device is offline: log type + message, not the stack trace
                Logger.LogWarning("HTTP publish to {Url} failed: {ErrorType}: {Error}", url, ex.GetType().Name, ex.Message);
                return false;
            }
        }
    }
}
