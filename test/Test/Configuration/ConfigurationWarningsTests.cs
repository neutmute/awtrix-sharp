using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using TransportOpenData;

namespace Test.Configuration
{
    public class ConfigurationWarningsTests
    {
        private static AwtrixConfig ConfigWith(params string[] appTypes) => new()
        {
            Devices = new[]
            {
                new DeviceConfig
                {
                    BaseTopic = "awtrix/clock1",
                    Apps = appTypes.Select(t => new AppConfig { Type = t }).ToList(),
                },
            },
        };

        [Fact]
        public void TripTimerApp_WithoutApiKey_Warns()
        {
            var warnings = ConfigurationWarnings.Get(ConfigWith("TripTimerApp"), new TransportOpenDataConfig { ApiKey = "" }, new SlackSettings());

            var warning = Assert.Single(warnings);
            Assert.Contains("TRANSPORTOPENDATA__APIKEY", warning);
        }

        [Fact]
        public void SlackStatusApp_WithoutAppToken_Warns()
        {
            var warnings = ConfigurationWarnings.Get(ConfigWith("SlackStatusApp"), new TransportOpenDataConfig(), new SlackSettings { AppToken = " " });

            var warning = Assert.Single(warnings);
            Assert.Contains("AWTRIXSHARP_SLACK__APPTOKEN", warning);
        }

        [Fact]
        public void ConfiguredSecrets_NoWarnings()
        {
            var warnings = ConfigurationWarnings.Get(
                ConfigWith("TripTimerApp", "SlackStatusApp"),
                new TransportOpenDataConfig { ApiKey = "key" },
                new SlackSettings { AppToken = "xapp-1" });

            Assert.Empty(warnings);
        }

        [Fact]
        public void AppsNotConfigured_NoWarnings_EvenWithoutSecrets()
        {
            var warnings = ConfigurationWarnings.Get(ConfigWith("DiurnalApp"), new TransportOpenDataConfig(), new SlackSettings());

            Assert.Empty(warnings);
        }
    }
}
