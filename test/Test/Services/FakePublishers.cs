using System.Net;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AwtrixSharpWeb.Domain;

namespace Test.Services
{
    /// <summary>
    /// Test double for HttpPublisher that captures the last published url/payload
    /// instead of making a real HTTP call. Overrides the abstract Publish(url, payload)
    /// so the stub IHttpClientFactory passed to the base is never used.
    /// </summary>
    public class FakeHttpPublisher : HttpPublisher
    {
        public string? LastUrl { get; private set; }
        public string? LastPayload { get; private set; }
        public int PublishCallCount { get; private set; }
        public bool ReturnValue { get; set; } = true;
        public Exception? ThrowOnPublish { get; set; }

        public FakeHttpPublisher()
            : base(NullLogger<HttpPublisher>.Instance, new StubHttpClientFactory(StubHttpMessageHandler.Returning(HttpStatusCode.OK)))
        {
        }

        public override Task<bool> Publish(string url, string payload)
        {
            LastUrl = url;
            LastPayload = payload;
            PublishCallCount++;
            if (ThrowOnPublish != null)
            {
                throw ThrowOnPublish;
            }
            return Task.FromResult(ReturnValue);
        }
    }

    /// <summary>
    /// Test double for MqttPublisher that captures the last published topic/payload
    /// instead of routing through a real MqttConnector/broker.
    /// </summary>
    public class FakeMqttPublisher : MqttPublisher
    {
        public string? LastUrl { get; private set; }
        public string? LastPayload { get; private set; }
        public int PublishCallCount { get; private set; }
        public bool ReturnValue { get; set; } = true;
        public Exception? ThrowOnPublish { get; set; }

        public FakeMqttPublisher()
            : base(new MqttConnector(NullLogger<MqttConnector>.Instance, Options.Create(new MqttSettings())), NullLogger<MqttPublisher>.Instance)
        {
        }

        public override Task<bool> Publish(string url, string payload)
        {
            LastUrl = url;
            LastPayload = payload;
            PublishCallCount++;
            if (ThrowOnPublish != null)
            {
                throw ThrowOnPublish;
            }
            return Task.FromResult(ReturnValue);
        }
    }
}
