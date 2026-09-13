using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using Moq;
using Test.Apps;

namespace Test.HostedServices
{
    /// <summary>
    /// Conductor.StartAsync over interface mocks: startup must never block or throw because of one app,
    /// the broker, or a device (CR-05, CR-06, CR-30).
    /// </summary>
    public class ConductorStartupTests
    {
        private static readonly DateTimeOffset HalfPastMidnight = new DateTimeOffset(2026, 9, 13, 0, 30, 0, TimeSpan.FromHours(10));

        [Fact]
        public async Task StartAsync_WhenMqttSubscribeNeverCompletes_StillCompletes()
        {
            var device = new DeviceConfig { BaseTopic = "awtrix/clock1" };
            var mqtt = new Mock<IMqttConnector>();
            mqtt.Setup(m => m.Subscribe(It.IsAny<string>())).Returns(new TaskCompletionSource().Task);
            var conductor = ConductorTestHelper.Create(
                new AwtrixConfig { Devices = new[] { device } },
                mqttConnector: mqtt.Object,
                clock: new MockClock(HalfPastMidnight));

            await Task.Run(() => conductor.StartAsync(CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Single(conductor.FindApps(AppNames.ButtonApp));
        }
    }
}
