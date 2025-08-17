using AwtrixSharpWeb.Domain;
using System.Net.Http;

namespace AwtrixSharpWeb.Services
{
    public abstract class AwtrixPublisher
    {
        ILogger _logger;

        protected AwtrixPublisher(ILogger logger)
        {
            _logger = logger;
        }

        public string ToJson(AwtrixAppMessage? message)
        {
            if (message == null)
            {
                return string.Empty;
            }
            else
            {
                return message.ToJson();
            }
        }

        public abstract Task<bool> Publish(string url, string payload);

        public async Task<bool> Publish(string url, AwtrixAppMessage? message)
        {
            var json = ToJson(message);
            var publisherType = this.GetType().Name;
            _logger.LogDebug("{publisherType} Publishing to {url} with payload: {json}", publisherType, url, json);
            return await Publish(url, json);
        }
    }
}
