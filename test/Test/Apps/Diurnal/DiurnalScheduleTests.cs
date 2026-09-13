using AwtrixSharpWeb.Apps.Diurnal;
using AwtrixSharpWeb.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Test.Apps.Diurnal
{
    public class DiurnalScheduleTests
    {
        private static readonly DateTime Day = new(2026, 9, 13, 0, 0, 0, DateTimeKind.Local);

        private static readonly Dictionary<string, string> Shipped = new()
        {
            ["0600"] = "Brightness=8",
            ["0700"] = "GlobalTextColor=#FFFFFF",
            ["1900"] = "GlobalTextColor=#FF0000",
            ["2100"] = "Brightness=1",
        };

        private static DateTime T(int hour, int minute, int dayOffset = 0) => Day.AddDays(dayOffset).Add(new TimeSpan(hour, minute, 0));

        private static DiurnalSchedule Parse(Dictionary<string, string> config) => DiurnalSchedule.Parse(config, NullLogger.Instance);

        private static void VerifyWarningLogged(Mock<ILogger> logger)
        {
            logger.Verify(l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
                Times.AtLeastOnce);
        }

        // ---------- Parse (CR-21) ----------

        [Fact]
        public void Parse_ValidConfig_ProducesSortedEntriesWithDeviceKeys()
        {
            var sut = Parse(new Dictionary<string, string>
            {
                ["2100"] = "Brightness=1",
                ["0600"] = "Brightness=8",
                ["0700"] = "GlobalTextColor=#FFFFFF",
            });

            Assert.Equal(new[] { new TimeSpan(6, 0, 0), new TimeSpan(7, 0, 0), new TimeSpan(21, 0, 0) }, sut.Entries.Select(e => e.Time));
            Assert.Equal("8", sut.Entries[0].Settings["BRI"]);
            Assert.Equal("#FFFFFF", sut.Entries[1].Settings["TCOL"]);
        }

        [Theory]
        [InlineData("Brightness=dim")]
        [InlineData("Brightness=300")]
        [InlineData("Brightness=-1")]
        [InlineData("Brightness=8.5")]
        public void Parse_InvalidBrightness_DropsEntryAndWarns(string value)
        {
            var logger = new Mock<ILogger>();

            var sut = DiurnalSchedule.Parse(new Dictionary<string, string> { ["2100"] = value }, logger.Object);

            Assert.True(sut.IsEmpty);
            VerifyWarningLogged(logger);
        }

        [Fact]
        public void Parse_MixedValidAndInvalidSettings_KeepsValidPart()
        {
            var sut = Parse(new Dictionary<string, string> { ["2100"] = "Brightness=dim;GlobalTextColor=#00FF00" });

            var entry = Assert.Single(sut.Entries);
            Assert.False(entry.Settings.ContainsKey("BRI"));
            Assert.Equal("#00FF00", entry.Settings["TCOL"]);
        }

        [Fact]
        public void Parse_UnknownSettingOnly_DropsEntryAndWarns()
        {
            var logger = new Mock<ILogger>();

            var sut = DiurnalSchedule.Parse(new Dictionary<string, string> { ["0600"] = "Brightnes=8" }, logger.Object);

            Assert.True(sut.IsEmpty);
            VerifyWarningLogged(logger);
        }

        [Theory]
        [InlineData("2400")]
        [InlineData("0660")]
        [InlineData("not-a-time")]
        [InlineData("")]
        public void Parse_InvalidTimeKey_DropsEntryAndWarns(string key)
        {
            var logger = new Mock<ILogger>();

            var sut = DiurnalSchedule.Parse(new Dictionary<string, string> { [key] = "Brightness=5" }, logger.Object);

            Assert.True(sut.IsEmpty);
            VerifyWarningLogged(logger);
        }

        [Fact]
        public void Parse_SettingNamesAreCaseInsensitive()
        {
            var sut = Parse(new Dictionary<string, string> { ["0600"] = "BRIGHTNESS=5; globaltextcolor=#123456" });

            var entry = Assert.Single(sut.Entries);
            Assert.Equal("5", entry.Settings["BRI"]);
            Assert.Equal("#123456", entry.Settings["TCOL"]);
        }

        [Fact]
        public void Parse_NullConfig_IsEmpty()
        {
            Assert.True(DiurnalSchedule.Parse(null, NullLogger.Instance).IsEmpty);
        }

        // ---------- StateAt (CR-20 startup) ----------

        [Fact]
        public void StateAt_0300_UsesYesterdayEveningSettings()
        {
            var state = Parse(Shipped).StateAt(T(3, 0));

            Assert.Equal("1", state["BRI"]);
            Assert.Equal("#FF0000", state["TCOL"]);
        }

        [Fact]
        public void StateAt_0630_CombinesTodayBrightnessWithYesterdayColour()
        {
            var state = Parse(Shipped).StateAt(T(6, 30));

            Assert.Equal("8", state["BRI"]);
            Assert.Equal("#FF0000", state["TCOL"]);
        }

        [Fact]
        public void StateAt_WithinEntryMinute_IncludesThatEntry()
        {
            var state = Parse(Shipped).StateAt(T(7, 0).AddSeconds(42));

            Assert.Equal("8", state["BRI"]);
            Assert.Equal("#FFFFFF", state["TCOL"]);
        }

        [Fact]
        public void StateAt_EmptySchedule_ReturnsEmptySettings()
        {
            Assert.Empty(DiurnalSchedule.Empty.StateAt(T(12, 0)));
        }

        // ---------- DueBetween (CR-20 runtime) ----------

        [Fact]
        public void DueBetween_StallAcrossEntry_AppliesMissedEntryOnly()
        {
            var due = Parse(Shipped).DueBetween(T(20, 59), T(21, 1));

            var setting = Assert.Single(due);
            Assert.Equal("BRI", setting.Key);
            Assert.Equal("1", setting.Value);
        }

        [Fact]
        public void DueBetween_WindowCrossesMidnight_AppliesMidnightEntry()
        {
            var sut = Parse(new Dictionary<string, string> { ["0000"] = "Brightness=3" });

            var due = sut.DueBetween(T(23, 59), T(0, 1, dayOffset: 1));

            Assert.Equal("3", due["BRI"]);
        }

        [Fact]
        public void DueBetween_StartIsExclusiveEndIsInclusive()
        {
            var sut = Parse(Shipped);

            Assert.Empty(sut.DueBetween(T(6, 0), T(6, 59)));
            Assert.Equal("#FFFFFF", sut.DueBetween(T(6, 59), T(7, 0))["TCOL"]);
        }

        [Fact]
        public void DueBetween_SecondsAreIgnored()
        {
            var due = Parse(Shipped).DueBetween(T(5, 59).AddSeconds(30), T(6, 0).AddSeconds(7));

            Assert.Equal("8", due["BRI"]);
        }

        [Fact]
        public void DueBetween_EmptyOrBackwardsWindow_ReturnsEmpty()
        {
            var sut = Parse(Shipped);

            Assert.Empty(sut.DueBetween(T(21, 0), T(21, 0)));
            Assert.Empty(sut.DueBetween(T(21, 30), T(20, 30)));
        }

        [Fact]
        public void DueBetween_SameKeyTwiceInWindow_LaterEntryWins()
        {
            // 0600 BRI=8 then 0700 TCOL white then 1900 TCOL red
            var due = Parse(Shipped).DueBetween(T(5, 0), T(20, 0));

            Assert.Equal("8", due["BRI"]);
            Assert.Equal("#FF0000", due["TCOL"]);
        }

        [Fact]
        public void DueBetween_WindowOfADayOrMore_ReturnsStateAtEnd()
        {
            var sut = Parse(Shipped);

            var due = sut.DueBetween(T(12, 0), T(6, 30, dayOffset: 3));

            Assert.Equal(sut.StateAt(T(6, 30, dayOffset: 3)), due);
        }

        [Fact]
        public void TruncateToMinute_DropsSecondsAndKeepsKind()
        {
            var value = new DateTime(2026, 9, 13, 6, 0, 59, 999, DateTimeKind.Local);

            var truncated = DiurnalSchedule.TruncateToMinute(value);

            Assert.Equal(new DateTime(2026, 9, 13, 6, 0, 0, DateTimeKind.Local), truncated);
            Assert.Equal(DateTimeKind.Local, truncated.Kind);
        }
    }
}
