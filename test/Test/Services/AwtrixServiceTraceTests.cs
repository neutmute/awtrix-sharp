using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Time.Testing;

namespace Test.Services
{
    public class AwtrixServiceTraceTests
    {
        private static readonly AwtrixAddress Mqtt = new() { BaseTopic = "awtrix/clock1" };

        private static (AwtrixService service, PublishTrace trace, FakeMqttPublisher mqtt) Create(bool deliver = true)
        {
            var trace = new PublishTrace();
            var mqtt = new FakeMqttPublisher { ReturnValue = deliver };
            var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero));
            return (new AwtrixService(new FakeHttpPublisher(), mqtt, null, trace, time), trace, mqtt);
        }

        [Fact]
        public async Task Notify_RecordsOperation_WithoutAppName()
        {
            var (service, trace, _) = Create();

            await service.Notify(Mqtt, new AwtrixAppMessage().SetText("hi"));

            var record = Assert.Single(trace.Recent());
            Assert.Equal("Notify", record.Operation);
            Assert.Null(record.AppName);
            Assert.Equal("awtrix/clock1", record.BaseTopic);
            Assert.True(record.Delivered);
            Assert.Equal(new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero), record.At);
            Assert.Contains("\"text\":\"hi\"", record.Request.Payload);
        }

        [Fact]
        public async Task AppUpdate_And_AppClear_RecordAppName()
        {
            var (service, trace, _) = Create();

            await service.AppUpdate(Mqtt, "MqttRenderApp", new AwtrixAppMessage().SetText("1"));
            await service.AppClear(Mqtt, "MqttRenderApp");

            Assert.Equal(new[] { "AppClear", "AppUpdate" }, trace.Recent().Select(r => r.Operation));
            Assert.All(trace.Recent(), r => Assert.Equal("MqttRenderApp", r.AppName));
            Assert.Equal("AppClear", trace.LastForApp("awtrix/clock1", "MqttRenderApp")!.Operation);
        }

        [Fact]
        public async Task Dismiss_Settings_Rtttl_RecordTheirOperations()
        {
            var (service, trace, _) = Create();

            await service.Dismiss(Mqtt);
            await service.Set(Mqtt, new AwtrixSettings().SetBrightness(3));
            await service.PlayRtttl(Mqtt, "a:d=4,o=5,b=100:c");

            Assert.Equal(new[] { "Rtttl", "Settings", "Dismiss" }, trace.Recent().Select(r => r.Operation));
        }

        [Fact]
        public async Task UndeliveredPublish_IsRecorded_AsNotDelivered()
        {
            var (service, trace, _) = Create(deliver: false);

            var result = await service.Notify(Mqtt, new AwtrixAppMessage().SetText("hi"));

            Assert.False(result);
            Assert.False(Assert.Single(trace.Recent()).Delivered);
        }

        [Fact]
        public async Task ThrowingPublisher_IsRecorded_AsNotDelivered()
        {
            var (service, trace, mqtt) = Create();
            mqtt.ThrowOnPublish = new InvalidOperationException("down");

            await service.Notify(Mqtt, new AwtrixAppMessage().SetText("hi"));

            Assert.False(Assert.Single(trace.Recent()).Delivered);
        }

        [Fact]
        public async Task NullArguments_RecordNothing()
        {
            var (service, trace, _) = Create();

            await service.Notify(Mqtt, null!);

            Assert.Empty(trace.Recent());
        }

        [Fact]
        public async Task WithoutTrace_StillPublishes()
        {
            var mqtt = new FakeMqttPublisher();
            var service = new AwtrixService(new FakeHttpPublisher(), mqtt);

            Assert.True(await service.Dismiss(Mqtt));
            Assert.Equal(1, mqtt.PublishCallCount);
        }
    }
}
