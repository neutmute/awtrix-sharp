using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Test.Apps.MqttRender;

namespace Test.HostedServices
{
    public class DeviceStateMonitorTests
    {
        private static string Fixture() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "ng-capabilities.json"));

        private static (DeviceStateMonitor monitor, Mock<IMqttConnector> mqtt, FakeTimeProvider time) Create(params string[] baseTopics)
        {
            var mqtt = new Mock<IMqttConnector>();
            mqtt.Setup(m => m.Subscribe(It.IsAny<string>())).Returns(Task.CompletedTask);
            var config = new AwtrixConfig { Devices = baseTopics.Select(t => new DeviceConfig { BaseTopic = t }).ToArray() };
            var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero));
            var monitor = new DeviceStateMonitor(mqtt.Object, Options.Create(config), NullLogger<DeviceStateMonitor>.Instance, time);
            return (monitor, mqtt, time);
        }

        private static void Raise(Mock<IMqttConnector> mqtt, string topic, string payload)
            => mqtt.Raise(m => m.MessageReceived += null, new object[] { MqttTestHelpers.CreateReceivedArgs(topic, payload) });

        [Fact]
        public async Task StartAsync_SubscribesThreeStateTopics_PerMqttDevice_AndNoneForHttp()
        {
            var (monitor, mqtt, _) = Create("awtrix/clock1", "http://192.168.1.10");

            await monitor.StartAsync(CancellationToken.None);

            mqtt.Verify(m => m.Subscribe("awtrix/clock1/state/capabilities"), Times.Once);
            mqtt.Verify(m => m.Subscribe("awtrix/clock1/state/settings"), Times.Once);
            mqtt.Verify(m => m.Subscribe("awtrix/clock1/state/device"), Times.Once);
            mqtt.Verify(m => m.Subscribe(It.IsAny<string>()), Times.Exactly(3));
        }

        [Fact]
        public void StateTopic_TrimsTrailingSlash()
        {
            Assert.Equal("awtrix/clock1/state/settings", DeviceStateMonitor.StateTopic("awtrix/clock1/", "settings"));
        }

        [Fact]
        public async Task HandlerIsAttached_BeforeSubscribing()
        {
            var (monitor, mqtt, _) = Create("awtrix/clock1");
            var attachedBeforeSubscribe = false;
            mqtt.SetupAdd(m => m.MessageReceived += It.IsAny<Func<MQTTnet.MqttApplicationMessageReceivedEventArgs, Task>>())
                .Callback(() => attachedBeforeSubscribe = true);
            mqtt.Setup(m => m.Subscribe(It.IsAny<string>())).Returns(() =>
            {
                Assert.True(attachedBeforeSubscribe, "Subscribe was called before the handler was attached");
                return Task.CompletedTask;
            });

            await monitor.StartAsync(CancellationToken.None);
        }

        [Fact]
        public async Task CapabilitiesMessage_UpdatesGet_AndRaisesChanged()
        {
            var (monitor, mqtt, time) = Create("awtrix/clock1");
            await monitor.StartAsync(CancellationToken.None);
            string? changed = null;
            monitor.Changed += topic => changed = topic;

            Raise(mqtt, "awtrix/clock1/state/capabilities", Fixture());

            var state = monitor.Get("awtrix/clock1");
            Assert.Equal(CapabilitiesSource.Device, state.Capabilities.Source);
            Assert.Equal(19, state.Capabilities.Effects.Length);
            Assert.Equal(time.GetUtcNow(), state.LastSeen);
            Assert.Equal("awtrix/clock1", changed);
        }

        [Fact]
        public async Task SettingsAndDevice_StoreRawJson()
        {
            var (monitor, mqtt, _) = Create("awtrix/clock1");
            await monitor.StartAsync(CancellationToken.None);

            Raise(mqtt, "awtrix/clock1/state/settings", "{\"brightness\":8}");
            Raise(mqtt, "awtrix/clock1/state/device", "{\"version\":\"1.0\"}");

            var state = monitor.Get("awtrix/clock1");
            Assert.Equal("{\"brightness\":8}", state.SettingsJson);
            Assert.Equal("{\"version\":\"1.0\"}", state.DeviceJson);
            Assert.Equal(CapabilitiesSource.BuiltIn, state.Capabilities.Source);
        }

        [Fact]
        public async Task MalformedCapabilities_KeepsPreviousValue()
        {
            var (monitor, mqtt, _) = Create("awtrix/clock1");
            await monitor.StartAsync(CancellationToken.None);
            Raise(mqtt, "awtrix/clock1/state/capabilities", "{\"effects\":[\"OnlyOne\"]}");

            Raise(mqtt, "awtrix/clock1/state/capabilities", "not json");

            Assert.Equal(new[] { "OnlyOne" }, monitor.Get("awtrix/clock1").Capabilities.Effects);
        }

        [Fact]
        public async Task UnrelatedTopic_IsIgnored()
        {
            var (monitor, mqtt, _) = Create("awtrix/clock1");
            await monitor.StartAsync(CancellationToken.None);
            var fired = 0;
            monitor.Changed += _ => fired++;

            Raise(mqtt, "awtrix/clock1/state/other", "{}");
            Raise(mqtt, "awtrix/clock2/state/settings", "{}");

            Assert.Equal(0, fired);
            Assert.Null(monitor.Get("awtrix/clock1").SettingsJson);
        }

        [Fact]
        public void Get_UnknownOrHttpDevice_ReturnsBuiltIn()
        {
            var (monitor, _, _) = Create("http://192.168.1.10");

            var state = monitor.Get("http://192.168.1.10");

            Assert.Same(DeviceCapabilities.BuiltIn, state.Capabilities);
            Assert.Null(state.LastSeen);
            Assert.Same(DeviceCapabilities.BuiltIn, monitor.Get("nope").Capabilities);
        }

        [Fact]
        public async Task ThrowingChangedSubscriber_DoesNotPropagate()
        {
            var (monitor, mqtt, _) = Create("awtrix/clock1");
            await monitor.StartAsync(CancellationToken.None);
            monitor.Changed += _ => throw new InvalidOperationException("boom");

            Raise(mqtt, "awtrix/clock1/state/settings", "{}");

            Assert.Equal("{}", monitor.Get("awtrix/clock1").SettingsJson);
        }

        [Fact]
        public async Task StopAsync_DetachesHandler()
        {
            var (monitor, mqtt, _) = Create("awtrix/clock1");
            await monitor.StartAsync(CancellationToken.None);

            await monitor.StopAsync(CancellationToken.None);
            Raise(mqtt, "awtrix/clock1/state/settings", "{\"after\":true}");

            Assert.Null(monitor.Get("awtrix/clock1").SettingsJson);
        }
    }
}
