using AwtrixSharpWeb.Services.TripPlanner;
using System;
using Xunit;

namespace Test.TripPlanner
{
    public class TimePlaceTests
    {
        [Fact]
        public void DefaultConstructor_SetsSentinelTimeAndEmptyPlace()
        {
            // Act
            var sut = new TimePlace();

            // Assert
            Assert.Equal(DateTimeOffset.Parse("2000-01-01"), sut.Time);
            Assert.Equal(string.Empty, sut.Place);
        }

        [Fact]
        public void Factory_SetsTimeAndPlace()
        {
            // Arrange
            var time = DateTimeOffset.Parse("2025-08-19T06:41:00+10:00");

            // Act
            var sut = TimePlace.Factory(time, "Central");

            // Assert
            Assert.Equal(time, sut.Time);
            Assert.Equal("Central", sut.Place);
        }

        [Fact]
        public void Factory_DefaultsPlaceToEmptyString_WhenNotSupplied()
        {
            // Arrange
            var time = DateTimeOffset.Parse("2025-08-19T06:41:00+10:00");

            // Act
            var sut = TimePlace.Factory(time);

            // Assert
            Assert.Equal(string.Empty, sut.Place);
        }

        [Theory]
        [InlineData("2025-08-19T06:41:00+10:00", "2025-08-19T06:41:00+10:00")] // already on a minute boundary
        [InlineData("2025-08-19T06:41:29+10:00", "2025-08-19T06:41:00+10:00")] // truncates down, does not round up
        [InlineData("2025-08-19T06:41:59+10:00", "2025-08-19T06:41:00+10:00")] // truncates down even at 59 seconds
        public void AsRounded_TruncatesSecondsDownToTheMinute(string input, string expected)
        {
            // Arrange
            var sut = TimePlace.Factory(DateTimeOffset.Parse(input), "Central");

            // Act
            var rounded = sut.AsRounded();

            // Assert
            Assert.Equal(DateTimeOffset.Parse(expected), rounded.Time);
            Assert.Equal("Central", rounded.Place);
        }

        [Fact]
        public void ToString_FormatsAs24HourTimeAndPlace()
        {
            // Arrange
            var sut = TimePlace.Factory(DateTimeOffset.Parse("2025-08-19T18:05:00+10:00"), "Circular Quay");

            // Act & Assert
            Assert.Equal("18:05 Circular Quay", sut.ToString());
        }
    }
}
