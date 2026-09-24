using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services.Firmware;

namespace AwtrixSharpWeb.Services
{
    public abstract class AwtrixPublisher
    {
        protected ILogger Logger { get; }

        protected AwtrixPublisher(ILogger logger)
        {
            Logger = logger;
        }

        /// <summary>
        /// Transport primitive. Contract: MUST NOT throw. Returns true only when the transport
        /// confirmed the hand-off (HTTP 2xx / MQTT client publish completed); every failure is
        /// logged by the implementation and reported as false.
        /// </summary>
        public abstract Task<bool> Publish(AwtrixRequest request);

        /// <summary>POST convenience for callers that only have a topic/URL and body (diagnostics, tests).</summary>
        public Task<bool> Publish(string url, string payload) => Publish(AwtrixRequest.Post(url, payload ?? string.Empty));
    }
}
