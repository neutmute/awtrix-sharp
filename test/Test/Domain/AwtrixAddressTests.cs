using AwtrixSharpWeb.Domain;
using Microsoft.Extensions.Configuration;

namespace Test.Domain
{
    public class AwtrixAddressTests
    {
        [Fact]
        public void ToString_ReturnsBaseTopic()
        {
            var address = new AwtrixAddress { BaseTopic = "awtrix/clock1" };

            Assert.Equal("awtrix/clock1", address.ToString());
        }

        [Fact]
        public void ToString_WhenBaseTopicNull_ReturnsNull()
        {
            var address = new AwtrixAddress();

            Assert.Null(address.ToString());
        }

        [Fact]
        public void DeviceConfig_InheritsBaseTopicFromAwtrixAddress()
        {
            var device = new DeviceConfig { BaseTopic = "awtrix/clock2" };

            Assert.Equal("awtrix/clock2", device.ToString());
            Assert.IsAssignableFrom<AwtrixAddress>(device);
        }

        [Fact]
        public void DeviceConfig_AppsDefaultsToEmptyList()
        {
            var device = new DeviceConfig();

            Assert.NotNull(device.Apps);
            Assert.Empty(device.Apps);
        }

        [Theory]
        [InlineData("http://192.168.1.50/api", true)]
        [InlineData("https://192.168.1.50/api", true)]
        [InlineData("HTTP://192.168.1.50/api", true)]
        [InlineData("awtrix/clock1", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsHttpTopic_DetectsHttpSchemesCaseInsensitively(string? topic, bool expected)
        {
            Assert.Equal(expected, AwtrixAddress.IsHttpTopic(topic));
            Assert.Equal(expected, new AwtrixAddress { BaseTopic = topic! }.IsHttp);
        }

        [Fact]
        public void Firmware_DefaultsToNull() => Assert.Null(new AwtrixAddress().Firmware);

        [Fact]
        public void Firmware_BindsAsPlainString()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Awtrix:Devices:0:BaseTopic"] = "awtrix/x",
                    ["Awtrix:Devices:0:Firmware"] = "NG",
                })
                .Build();

            var config = configuration.GetSection("Awtrix").Get<AwtrixConfig>()!;

            Assert.Equal("NG", config.Devices[0].Firmware);
        }
    }
}
