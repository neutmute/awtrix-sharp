using TransportOpenData;
using Xunit;

namespace TransportOpenData.Tests.Config
{
    public class TransportOpenDataConfigTests
    {
        [Fact]
        public void DefaultConstructor_SetsExpectedDefaultBaseUrl()
        {
            // Act
            var sut = new TransportOpenDataConfig();

            // Assert
            Assert.Equal("https://api.transport.nsw.gov.au/v1", sut.BaseUrl);
        }

        [Fact]
        public void DefaultConstructor_SetsEmptyApiKey()
        {
            // Act
            var sut = new TransportOpenDataConfig();

            // Assert
            Assert.Equal(string.Empty, sut.ApiKey);
        }

        [Fact]
        public void BaseUrl_CanBeOverridden()
        {
            // Arrange
            var sut = new TransportOpenDataConfig();

            // Act
            sut.BaseUrl = "https://example.test/v2";

            // Assert
            Assert.Equal("https://example.test/v2", sut.BaseUrl);
        }

        [Fact]
        public void ApiKey_CanBeOverridden()
        {
            // Arrange
            var sut = new TransportOpenDataConfig();

            // Act
            sut.ApiKey = "my-secret-key";

            // Assert
            Assert.Equal("my-secret-key", sut.ApiKey);
        }
    }
}
