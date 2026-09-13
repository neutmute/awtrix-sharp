using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AwtrixSharpWeb.Domain;

namespace Test.Services
{
    /// <summary>
    /// Test double for HttpPublisher that captures the last published url/payload
    /// instead of making a real HTTP call. HttpPublisher owns its HttpClient
    /// internally (no injection seam), so we override the abstract Publish(url, payload)
    /// method to intercept before any real network I/O happens.
    /// </summary>
    public class FakeHttpPublisher : HttpPublisher
    {
        public string? LastUrl { get; private set; }
        public string? LastPayload { get; private set; }
        public int PublishCallCount { get; private set; }
        public bool ReturnValue { get; set; } = true;

        public FakeHttpPublisher() : base(NullLogger<HttpPublisher>.Instance)
        {
        }

        public override Task<bool> Publish(string url, string payload)
        {
            LastUrl = url;
            LastPayload = payload;
            PublishCallCount++;
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

        public FakeMqttPublisher()
            : base(new MqttConnector(NullLogger<MqttConnector>.Instance, Options.Create(new MqttSettings())), NullLogger<MqttPublisher>.Instance)
        {
        }

        public override Task<bool> Publish(string url, string payload)
        {
            LastUrl = url;
            LastPayload = payload;
            PublishCallCount++;
            return Task.FromResult(ReturnValue);
        }
    }
}
