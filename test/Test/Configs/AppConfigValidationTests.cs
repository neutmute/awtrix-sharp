using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Apps.TripTimer;

namespace Test.Configs
{
    public class AppConfigValidationTests
    {
        private static T Build<T>(Dictionary<string, string> keys, string type) where T : AppConfig, new()
        {
            var source = AppConfig.Empty().WithName(type);
            foreach (var kvp in keys)
            {
                source.Config.Add(kvp.Key, kvp.Value);
            }
            return source.As<T>();
        }

        /// <summary>The shipped appsettings.json TripTimerApp config</summary>
        private static Dictionary<string, string> ShippedTripTimer() => new()
        {
            ["CronSchedule"] = "10 6 * * 1-5",
            ["ActiveTime"] = "01:00:00",
            ["StopIdOrigin"] = "200060",
            ["StopIdDestination"] = "200070",
            ["TimeToOrigin"] = "00:14:00",
            ["TimeToPrepare"] = "00:08:00",
        };

        private static Dictionary<string, string> ShippedMqttRender() => new()
        {
            ["CronSchedule"] = "0 8 * * *",
            ["ActiveTime"] = "09:00:00",
            ["ReadTopic"] = "openhab/fronius/grid-surplus",
        };

        [Fact]
        public void ShippedConfigs_AreValid()
        {
            Assert.Empty(Build<TripTimerAppConfig>(ShippedTripTimer(), "TripTimerApp").Validate());
            Assert.Empty(Build<MqttAppConfig>(ShippedMqttRender(), "MqttRenderApp").Validate());
        }

        [Fact]
        public void BaseAppConfig_HasNoRules()
        {
            Assert.Empty(new AppConfig().Validate());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("61 * * * *")]
        [InlineData("not a cron")]
        public void CronSchedule_MissingOrInvalid_IsReported(string? cron)
        {
            var keys = ShippedMqttRender();
            if (cron is null) keys.Remove("CronSchedule"); else keys["CronSchedule"] = cron;

            var errors = Build<MqttAppConfig>(keys, "MqttRenderApp").Validate();

            Assert.Contains(errors, e => e.StartsWith("CronSchedule:"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("abc")]
        [InlineData("00:00:00")]
        [InlineData("-00:05:00")]
        public void ActiveTime_MissingInvalidOrNotPositive_IsReported(string? activeTime)
        {
            var keys = ShippedMqttRender();
            if (activeTime is null) keys.Remove("ActiveTime"); else keys["ActiveTime"] = activeTime;

            var errors = Build<MqttAppConfig>(keys, "MqttRenderApp").Validate();

            Assert.Contains(errors, e => e.StartsWith("ActiveTime:"));
        }

        [Fact]
        public void ActiveTime_InvalidValue_ErrorQuotesTheValue()
        {
            var keys = ShippedMqttRender();
            keys["ActiveTime"] = "abc";

            var errors = Build<MqttAppConfig>(keys, "MqttRenderApp").Validate();

            Assert.Contains(errors, e => e.Contains("'abc'"));
        }

        [Fact]
        public void MqttApp_MissingReadTopic_IsReported()
        {
            var keys = ShippedMqttRender();
            keys.Remove("ReadTopic");

            Assert.Contains(Build<MqttAppConfig>(keys, "MqttRenderApp").Validate(), e => e.StartsWith("ReadTopic:"));
        }

        [Theory]
        [InlineData("StopIdOrigin")]
        [InlineData("StopIdDestination")]
        public void TripTimer_MissingStopId_IsReported(string key)
        {
            var keys = ShippedTripTimer();
            keys.Remove(key);

            Assert.Contains(Build<TripTimerAppConfig>(keys, "TripTimerApp").Validate(), e => e.StartsWith(key + ":"));
        }

        [Fact]
        public void TripTimer_MissingTravelTimes_AreOptional_AndDefaultToZero()
        {
            var keys = ShippedTripTimer();
            keys.Remove("TimeToOrigin");
            keys.Remove("TimeToPrepare");

            var config = Build<TripTimerAppConfig>(keys, "TripTimerApp");

            Assert.Empty(config.Validate());
            Assert.Equal(TimeSpan.Zero, config.TimeToOrigin);
            Assert.Equal(TimeSpan.Zero, config.TimeToPrepare);
        }

        [Theory]
        [InlineData("TimeToOrigin", "soon")]
        [InlineData("TimeToPrepare", "-00:05:00")]
        public void TripTimer_InvalidTravelTime_IsReported(string key, string value)
        {
            var keys = ShippedTripTimer();
            keys[key] = value;

            Assert.Contains(Build<TripTimerAppConfig>(keys, "TripTimerApp").Validate(), e => e.StartsWith(key + ":"));
        }

        [Fact]
        public void EnsureValid_Throws_WithAppTypeDeviceAndKey()
        {
            var keys = ShippedTripTimer();
            keys.Remove("StopIdOrigin");
            var config = Build<TripTimerAppConfig>(keys, "TripTimerApp");

            var ex = Assert.Throws<AppConfigValidationException>(() => config.EnsureValid("awtrix/clock1"));

            Assert.Contains("TripTimerApp", ex.Message);
            Assert.Contains("awtrix/clock1", ex.Message);
            Assert.Contains("StopIdOrigin", ex.Message);
            Assert.Equal("TripTimerApp", ex.AppType);
            Assert.Equal("awtrix/clock1", ex.Device);
            Assert.Single(ex.Errors);
        }

        [Fact]
        public void EnsureValid_ValidConfig_DoesNotThrow()
        {
            Build<TripTimerAppConfig>(ShippedTripTimer(), "TripTimerApp").EnsureValid("awtrix/clock1");
        }
    }
}
