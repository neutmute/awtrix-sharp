using System.Globalization;
using AwtrixSharpWeb.Services.TripPlanner;

namespace Test.TripPlanner
{
    /// <summary>
    /// CR-26: Transport NSW speaks Sydney wall-clock time, whatever the host's TZ.
    /// </summary>
    public class TransportTimeTests
    {
        [Theory]
        [InlineData("2025-08-15T20:22:00Z", "20250816", "0622")]      // UTC host at 06:22 AEST: the CR-26 scenario
        [InlineData("2025-08-16T06:22:00+10:00", "20250816", "0622")]
        [InlineData("2025-08-16T01:52:00+05:30", "20250816", "0622")] // any offset, same instant
        [InlineData("2024-10-05T16:30:00Z", "20241006", "0330")]      // just after DST starts (AEDT)
        [InlineData("2024-04-06T15:30:00Z", "20240407", "0230")]      // 02:30 AEDT, before clocks go back
        [InlineData("2024-04-06T16:30:00Z", "20240407", "0230")]      // 02:30 AEST, after clocks go back
        public void ToQuery_FormatsSydneyWallClock(string instant, string itdDate, string itdTime)
        {
            var query = TransportTime.ToQuery(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture));

            Assert.Equal((itdDate, itdTime), query);
        }

        [Fact]
        public void ToQuery_UsesGregorianDigits_WhateverTheCurrentCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("th-TH"); // Buddhist calendar: the year would print as 2568

                Assert.Equal(("20250816", "0622"), TransportTime.ToQuery(DateTimeOffset.Parse("2025-08-15T20:22:00Z", CultureInfo.InvariantCulture)));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Theory]
        [InlineData("2025-09-01T06:00:00", "2025-09-01T06:00:00+10:00")] // no offset: Sydney wall clock (AEST)
        [InlineData("2025-09-01 06:41", "2025-09-01T06:41:00+10:00")]
        [InlineData("2025-01-02T06:00:00", "2025-01-02T06:00:00+11:00")] // AEDT
        [InlineData("2025-09-01T06:00:00Z", "2025-09-01T06:00:00+00:00")] // explicit instant kept
        [InlineData("2025-09-01T06:00:00+08:00", "2025-09-01T06:00:00+08:00")]
        public void TryParseQuery_OffsetlessIsSydney_OtherwiseTheGivenInstant(string value, string expected)
        {
            Assert.True(TransportTime.TryParseQuery(value, out var parsed));

            var expectedInstant = DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture);
            Assert.Equal(expectedInstant, parsed);
            if (!value.EndsWith("Z") && !value.Contains('+'))
            {
                Assert.Equal(expectedInstant.Offset, parsed.Offset);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-date")]
        public void TryParseQuery_RejectsGarbage(string? value)
        {
            Assert.False(TransportTime.TryParseQuery(value, out _));
        }

        [Fact]
        public void TryParseApiTime_ReturnsTheInstantWithTheSydneyOffset()
        {
            Assert.True(TransportTime.TryParseApiTime("2025-08-16T05:37:54Z", out var parsed));

            Assert.Equal(DateTimeOffset.Parse("2025-08-16T15:37:54+10:00", CultureInfo.InvariantCulture), parsed);
            Assert.Equal(TimeSpan.FromHours(10), parsed.Offset);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(" ")]
        [InlineData("soon")]
        public void TryParseApiTime_RejectsMissingOrGarbage(string? value)
        {
            Assert.False(TransportTime.TryParseApiTime(value, out _));
        }
    }
}
