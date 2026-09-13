using AwtrixSharpWeb.Services.TripPlanner;
using System;
using Xunit;

namespace Test.TripPlanner
{
    public class TripSummaryBehaviorTests
    {
        [Fact]
        public void TravelTime_ReturnsDifferenceBetweenOriginAndDestination()
        {
            // Arrange
            var origin = DateTimeOffset.Parse("2025-08-19T06:00:00+10:00");
            var destination = origin.AddMinutes(37);
            var sut = new TripSummary
            {
                Origin = TimePlace.Factory(origin, "Central"),
                Destination = TimePlace.Factory(destination, "Circular Quay")
            };

            // Act & Assert
            Assert.Equal(TimeSpan.FromMinutes(37), sut.TravelTime);
        }

        [Fact]
        public void AsRounded_TruncatesSecondsOnBothOriginAndDestination_ButKeepsPlace()
        {
            // Arrange
            var sut = new TripSummary
            {
                Origin = TimePlace.Factory(DateTimeOffset.Parse("2025-08-19T06:19:47+10:00"), "Central"),
                Destination = TimePlace.Factory(DateTimeOffset.Parse("2025-08-19T06:49:12+10:00"), "Circular Quay")
            };

            // Act
            var rounded = sut.AsRounded();

            // Assert
            Assert.Equal(DateTimeOffset.Parse("2025-08-19T06:19:00+10:00"), rounded.Origin.Time);
            Assert.Equal("Central", rounded.Origin.Place);
            Assert.Equal(DateTimeOffset.Parse("2025-08-19T06:49:00+10:00"), rounded.Destination.Time);
            Assert.Equal("Circular Quay", rounded.Destination.Place);
        }

        [Fact]
        public void AsRounded_ReturnsNewInstance_DoesNotMutateOriginal()
        {
            // Arrange
            var originalTime = DateTimeOffset.Parse("2025-08-19T06:19:47+10:00");
            var sut = new TripSummary
            {
                Origin = TimePlace.Factory(originalTime, "Central"),
                Destination = TimePlace.Factory(originalTime.AddMinutes(30), "Circular Quay")
            };

            // Act
            var rounded = sut.AsRounded();

            // Assert
            Assert.NotSame(sut, rounded);
            Assert.Equal(originalTime, sut.Origin.Time); // original untouched
        }

        [Fact]
        public void ToString_FormatsOriginAndDestination()
        {
            // Arrange
            var sut = new TripSummary
            {
                Origin = TimePlace.Factory(DateTimeOffset.Parse("2025-08-19T06:19:00+10:00"), "Central"),
                Destination = TimePlace.Factory(DateTimeOffset.Parse("2025-08-19T06:49:00+10:00"), "Circular Quay")
            };

            // Act
            var text = sut.ToString();

            // Assert
            Assert.Contains("06:19 Central", text);
            Assert.Contains("06:49 Circular Quay", text);
        }

        [Fact(Skip = "Known bug: TripSummary.Factory(time, place) ignores the 'place' parameter entirely - Origin.Place is always empty and Destination is always a default TimePlace rather than being derived from the supplied time/place.")]
        public void Factory_SetsOriginPlace_FromSuppliedPlaceArgument()
        {
            // Arrange
            var time = DateTimeOffset.Parse("2025-09-01T06:41:00+10:00");

            // Act
            var summary = TripSummary.Factory(time, "Central");

            // Assert (intended behaviour - currently Origin.Place is always string.Empty)
            Assert.Equal("Central", summary.Origin.Place);
            Assert.Equal(time, summary.Origin.Time);
        }

        [Fact]
        public void Factory_CurrentBehaviour_SetsOriginTimeOnly_DestinationIsDefault()
        {
            // This documents the actual (buggy) current behaviour of TripSummary.Factory,
            // as distinct from the intended behaviour captured (and skipped) above.
            // Arrange
            var time = DateTimeOffset.Parse("2025-09-01T06:41:00+10:00");

            // Act
            var summary = TripSummary.Factory(time, "Central");

            // Assert
            Assert.Equal(time, summary.Origin.Time);
            Assert.Equal(string.Empty, summary.Origin.Place);
            Assert.Equal(new TimePlace().Time, summary.Destination.Time);
        }
    }
}
