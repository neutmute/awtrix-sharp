using AwtrixSharpWeb.Domain;

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
    }
}
