using AwtrixSharpWeb.Apps.Configs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Test.Domain
{
    public class TripTimerAppConfigTests
    {
        [Fact]
        public void As_Method_Converts_Dictionary_To_TripTimerAppConfig_Successfully()
        {
            // Arrange
            var basicKeysConfig = new AppConfigKeys
            {
                { "Name", "TripPlanner" },
                { "CronSchedule", "0 30 7 * * 1-5" },
                { "ActiveTime", "00:05:00" },
                { "StopIdOrigin", "200080" },
                { "StopIdDestination", "200060" },
                { "TimeToOrigin", "00:15:00" },
                { "TimeToPrepare", "00:30:00" }
            };

            var basicConfig = AppConfig.Empty().WithName("TripPlanner");
            basicConfig.Keys = basicKeysConfig;

            // Act
            TripTimerAppConfig tripConfig = basicConfig.As<TripTimerAppConfig>();

            // Assert
            Assert.Equal("TripPlanner", tripConfig.Name);
            Assert.Equal("0 30 7 * * 1-5", tripConfig.CronSchedule);
            Assert.Equal(TimeSpan.FromMinutes(5), tripConfig.ActiveTime);
            Assert.Equal("200080", tripConfig.StopIdOrigin);
            Assert.Equal("200060", tripConfig.StopIdDestination);
            Assert.Equal(TimeSpan.FromMinutes(15), tripConfig.TimeToOrigin);
            Assert.Equal(TimeSpan.FromMinutes(30), tripConfig.TimeToPrepare);
            
            // Verify dictionary values are also preserved
            Assert.Equal("TripPlanner", tripConfig.Name);
            Assert.Equal("0 30 7 * * 1-5", tripConfig.Keys["CronSchedule"]);
            Assert.Equal("00:05:00", tripConfig.Keys["ActiveTime"]);
            Assert.Equal("200080", tripConfig.Keys["StopIdOrigin"]);
            Assert.Equal("200060", tripConfig.Keys["StopIdDestination"]);
            Assert.Equal("00:15:00", tripConfig.Keys["TimeToOrigin"]);
            Assert.Equal("00:30:00", tripConfig.Keys["TimeToPrepare"]);
        }

        [Fact]
        public void As_Method_Preserves_Dictionary_Keys_Not_Mapped_To_Properties()
        {
            // Arrange
            var keysConfig = new AppConfigKeys
            {
                { "Name", "TripPlanner" },
                { "CustomSetting", "CustomValue" }, // This doesn't map to any property
                { "CronSchedule", "0 30 7 * * 1-5" },
                { "ActiveTime", "00:05:00" }
            };

            var basicConfig = AppConfig.Empty().WithName("TripPlanner"); 
            basicConfig.Keys = keysConfig;

            // Act
            TripTimerAppConfig tripConfig = basicConfig.As<TripTimerAppConfig>();

            // Assert
            Assert.Equal("TripPlanner", tripConfig.Name);
            Assert.Equal("0 30 7 * * 1-5", tripConfig.CronSchedule);
            Assert.Equal(TimeSpan.FromMinutes(5), tripConfig.ActiveTime);
            
            // Verify the custom setting is preserved in the dictionary
            Assert.Equal("CustomValue", tripConfig.Keys["CustomSetting"]);
        }

        [Fact]
        public void As_Method_Handles_TimeSpan_Conversion_Correctly()
        {
            // Arrange
            var keysConfig = new AppConfigKeys
            {
                { "ActiveTime", "01:30:45" },       // 1 hour, 30 minutes, 45 seconds
                { "TimeToOrigin", "00:45:30" },     // 45 minutes, 30 seconds
                { "TimeToPrepare", "02:00:00" }     // 2 hours
            };

            var basicConfig = AppConfig.Empty().WithName("TripPlanner");
            basicConfig.Keys = keysConfig;

            // Act
            TripTimerAppConfig tripConfig = basicConfig.As<TripTimerAppConfig>();

            // Assert
            Assert.Equal(TimeSpan.Parse("01:30:45"), tripConfig.ActiveTime);
            Assert.Equal(TimeSpan.Parse("00:45:30"), tripConfig.TimeToOrigin);
            Assert.Equal(TimeSpan.Parse("02:00:00"), tripConfig.TimeToPrepare);
            
            // Verify total values are correctly calculated
            Assert.Equal(90.75, tripConfig.ActiveTime.TotalMinutes);
            Assert.Equal(45.5, tripConfig.TimeToOrigin.TotalMinutes);
            Assert.Equal(120, tripConfig.TimeToPrepare.TotalMinutes);
        }

        [Fact]
        public void As_Method_Returns_New_Instance_With_Same_Values()
        {
            // Arrange
            var keysConfig = new AppConfigKeys
            {
                { "Name", "TripPlanner" },
                { "CronSchedule", "0 30 7 * * 1-5" },
                { "ActiveTime", "00:05:00" }
            };

            var basicConfig = AppConfig.Empty().WithName("TripPlanner");
            basicConfig.Keys = keysConfig;

            // Act
            var converted = basicConfig.As<ScheduledAppConfig>();

            // Assert
            Assert.NotSame(basicConfig, converted); // Verify it's a new instance
            Assert.IsType<ScheduledAppConfig>(converted); // Verify it's the right type
            Assert.Equal("TripPlanner", converted.Name);
            Assert.Equal("0 30 7 * * 1-5", converted.CronSchedule);
            Assert.Equal(TimeSpan.FromMinutes(5), converted.ActiveTime);
        }
    }
}
