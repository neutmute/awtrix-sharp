using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;

namespace Test.HostedServices
{
    /// <summary>
    /// CR-14: a blank SlackStatusApp SlackUserId is filled from Slack:UserId (which is where
    /// AWTRIXSHARP_SLACK__USERID arrives through the prefixed provider). An explicit value still wins.
    /// </summary>
    public class ConductorSlackSettingsTests
    {
        private static AwtrixConfig ConfigWithSlackUserId(string slackUserId)
        {
            var app = new AppConfig { Type = AppNames.SlackStatusApp };
            app.Config.Add("SlackUserId", slackUserId);
            return new AwtrixConfig
            {
                Devices = new[] { new DeviceConfig { BaseTopic = "awtrix/clock1", Apps = new List<AppConfig> { app } } },
            };
        }

        [Fact]
        public async Task BlankSlackUserId_IsFilledFromSlackSettings()
        {
            var conductor = ConductorTestHelper.Create(
                ConfigWithSlackUserId(""),
                slackSettings: new SlackSettings { UserId = "U-FROM-CONFIG" });

            await conductor.StartAsync(CancellationToken.None);
            try
            {
                var app = Assert.Single(conductor.FindApps(AppNames.SlackStatusApp));
                Assert.Equal("U-FROM-CONFIG", ((AppConfig)app.GetConfig()).Config.Get("SlackUserId"));
            }
            finally
            {
                await conductor.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task ExplicitSlackUserId_IsKept()
        {
            var conductor = ConductorTestHelper.Create(
                ConfigWithSlackUserId("U-EXPLICIT"),
                slackSettings: new SlackSettings { UserId = "U-FROM-CONFIG" });

            await conductor.StartAsync(CancellationToken.None);
            try
            {
                var app = Assert.Single(conductor.FindApps(AppNames.SlackStatusApp));
                Assert.Equal("U-EXPLICIT", ((AppConfig)app.GetConfig()).Config.Get("SlackUserId"));
            }
            finally
            {
                await conductor.StopAsync(CancellationToken.None);
            }
        }
    }
}
