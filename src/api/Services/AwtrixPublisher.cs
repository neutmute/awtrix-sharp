using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Services
{
    public abstract class AwtrixPublisher
    {
        protected ILogger Logger { get; }

        protected AwtrixPublisher(ILogger logger)
        {
            Logger = logger;
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

        /// <summary>
        /// Address of a custom app for this transport. Default is the MQTT topic form.
        /// </summary>
        public virtual string BuildCustomAppUrl(string baseTopic, string appName)
        {
            return $"{baseTopic}/custom/{appName}";
        }

        /// <summary>
        /// Transport primitive. Contract: MUST NOT throw. Returns true only when the transport
        /// confirmed the hand-off (HTTP 2xx / MQTT client publish completed); every failure is
        /// logged by the implementation and reported as false.
        /// </summary>
        public abstract Task<bool> Publish(string url, string payload);

        public async Task<bool> Publish(string url, AwtrixAppMessage? message)
        {
            var json = ToJson(message);
            var publisherType = this.GetType().Name;
            Logger.LogDebug("{publisherType} Publishing to {url} with payload: {json}", publisherType, url, json);
            return await Publish(url, json);
        }
    }
}
