using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.TripTimer;
using AwtrixSharpWeb.Domain;
using Microsoft.Extensions.Configuration;

namespace Test.Configs
{
    /// <summary>
    /// CR-22: IConfiguration keys are case-insensitive and the env-var provider keeps the variable's casing
    /// (AWTRIXSHARP_..._CONFIG__STOPIDORIGIN), so the app config dictionaries must ignore case too.
    /// </summary>
    public class ConfigKeyCaseTests
    {
        private static AppConfig BindFirstApp(params Dictionary<string, string?>[] layers)
        {
            var builder = new ConfigurationBuilder();
            foreach (var layer in layers)
            {
                builder.AddInMemoryCollection(layer);
            }

            var awtrix = builder.Build().GetSection("Awtrix").Get<AwtrixConfig>();
            Assert.NotNull(awtrix);
            return awtrix!.Devices[0].Apps[0];
        }

        [Fact]
        public void AppConfigKeys_Get_IgnoresCase()
        {
            var sut = new AppConfigKeys { { "StopIdOrigin", "200060" } };

            Assert.Equal("200060", sut.Get("STOPIDORIGIN"));
        }

        [Fact]
        public void AppConfigKeys_Clone_KeepsCaseInsensitivity()
        {
            var clone = new AppConfigKeys { { "StopIdOrigin", "200060" } }.Clone();

            Assert.Equal("200060", clone.Get("stopidorigin"));
        }

        [Fact]
        public void ValueMap_LowercaseValueMatcherKey_IsUsed()
        {
            var sut = new ValueMap { { "valueMatcher", "^-" } };

            Assert.Equal("^-", sut.ValueMatcher);
            Assert.True(sut.IsMatch("-12"));
        }

        [Fact]
        public void ValueMap_Clone_KeepsCaseInsensitivity()
        {
            var clone = new ValueMap { { "valueMatcher", "busy" } }.Clone();

            Assert.Equal("busy", clone.ValueMatcher);
        }

        [Fact]
        public void Binding_EnvVarStyleUppercaseKey_ReachesTypedProperty()
        {
            var app = BindFirstApp(new Dictionary<string, string?>
            {
                ["Awtrix:Devices:0:BaseTopic"] = "awtrix/clock1",
                ["Awtrix:Devices:0:Apps:0:Type"] = "TripTimerApp",
                ["Awtrix:Devices:0:Apps:0:Config:STOPIDORIGIN"] = "200060",
            });

            Assert.Equal("200060", app.As<TripTimerAppConfig>().StopIdOrigin);
        }

        [Fact]
        public void Binding_CamelCaseCronSchedule_ReachesTypedProperty()
        {
            var app = BindFirstApp(new Dictionary<string, string?>
            {
                ["Awtrix:Devices:0:BaseTopic"] = "awtrix/clock1",
                ["Awtrix:Devices:0:Apps:0:Type"] = "MqttRenderApp",
                ["Awtrix:Devices:0:Apps:0:Config:cronSchedule"] = "0 8 * * *",
            });

            Assert.Equal("0 8 * * *", app.As<ScheduledAppConfig>().CronSchedule);
        }

        [Fact]
        public void Binding_LaterUppercaseOverride_Wins()
        {
            var app = BindFirstApp(
                new Dictionary<string, string?>
                {
                    ["Awtrix:Devices:0:BaseTopic"] = "awtrix/clock1",
                    ["Awtrix:Devices:0:Apps:0:Type"] = "TripTimerApp",
                    ["Awtrix:Devices:0:Apps:0:Config:StopIdOrigin"] = "200060",
                },
                new Dictionary<string, string?>
                {
                    ["Awtrix:Devices:0:Apps:0:Config:STOPIDORIGIN"] = "999999",
                });

            Assert.Equal("999999", app.As<TripTimerAppConfig>().StopIdOrigin);
        }

        [Fact]
        public void Binding_LowercaseValueMatcher_Matches()
        {
            var app = BindFirstApp(new Dictionary<string, string?>
            {
                ["Awtrix:Devices:0:BaseTopic"] = "awtrix/clock1",
                ["Awtrix:Devices:0:Apps:0:Type"] = "MqttRenderApp",
                ["Awtrix:Devices:0:Apps:0:ValueMaps:0:valueMatcher"] = "^-",
                ["Awtrix:Devices:0:Apps:0:ValueMaps:0:Icon"] = "52465",
            });

            Assert.NotNull(app.FindMatchingValueMap("-3.2"));
        }
    }
}
