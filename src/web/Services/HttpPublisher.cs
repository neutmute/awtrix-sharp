using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;

namespace AwtrixSharpWeb.Services
{
    public class HttpPublisher : AwtrixPublisher
    {
        private readonly HttpClient _httpClient;

        public HttpPublisher(ILogger<HttpPublisher> logger) : base(logger)
        {
            _httpClient = new HttpClient();
        }

        public override async Task<bool> Publish(string url, string payload)
        {
            var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            return response.IsSuccessStatusCode;
        }
    }
}
