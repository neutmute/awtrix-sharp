using AwtrixSharpWeb.Services;

namespace Test.Services
{
    public class PublishTraceTests
    {
        private static PublishRecord Rec(int seq, string topic = "awtrix/clock1", string op = "AppUpdate", string? app = "MqttRenderApp", bool delivered = true)
            => new(DateTimeOffset.UnixEpoch.AddSeconds(seq), topic, op, app, AwtrixRequest.Post($"{topic}/x", $"{{\"seq\":{seq}}}"), delivered);

        [Fact]
        public void Recent_IsNewestFirst_AndBounded()
        {
            var trace = new PublishTrace(capacity: 3);
            for (var i = 1; i <= 5; i++) trace.Record(Rec(i));

            var recent = trace.Recent();

            Assert.Equal(new[] { 5, 4, 3 }, recent.Select(r => (int)(r.At - DateTimeOffset.UnixEpoch).TotalSeconds));
        }

        [Fact]
        public void LastForApp_DistinguishesDevices_RunningTheSameApp()
        {
            var trace = new PublishTrace();
            trace.Record(Rec(1, "awtrix/clock1"));
            trace.Record(Rec(2, "awtrix/clock2"));
            trace.Record(Rec(3, "awtrix/clock1"));

            Assert.Equal(3, (int)(trace.LastForApp("awtrix/clock1", "MqttRenderApp")!.At - DateTimeOffset.UnixEpoch).TotalSeconds);
            Assert.Equal(2, (int)(trace.LastForApp("awtrix/clock2", "MqttRenderApp")!.At - DateTimeOffset.UnixEpoch).TotalSeconds);
            Assert.Null(trace.LastForApp("awtrix/clock3", "MqttRenderApp"));
            Assert.Null(trace.LastForApp("awtrix/clock1", "Other"));
        }

        [Fact]
        public void LastForApp_IgnoresNotifyRecords_ButKeepsFailedSends()
        {
            var trace = new PublishTrace();
            trace.Record(Rec(1, op: "AppUpdate", delivered: false));
            trace.Record(Rec(2, op: "Notify", app: null));

            var last = trace.LastForApp("awtrix/clock1", "MqttRenderApp");

            Assert.NotNull(last);
            Assert.False(last!.Delivered);
            Assert.Equal(2, trace.Recent().Count);
        }

        [Fact]
        public void Changed_FiresOnRecord()
        {
            var trace = new PublishTrace();
            var fired = 0;
            trace.Changed += () => fired++;

            trace.Record(Rec(1));

            Assert.Equal(1, fired);
        }

        [Fact]
        public void Changed_ThrowingSubscriber_DoesNotPropagate()
        {
            var trace = new PublishTrace();
            trace.Changed += () => throw new InvalidOperationException("boom");

            trace.Record(Rec(1));

            Assert.Single(trace.Recent());
        }
    }
}
