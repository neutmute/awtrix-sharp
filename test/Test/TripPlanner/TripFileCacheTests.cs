using System.Globalization;
using System.Text.Json;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Logging;
using Moq;

namespace Test.TripPlanner
{
    /// <summary>
    /// CR-37: the opt-in cache never breaks a lookup (null means "use the API"), keys on the Sydney hour, rejects unsafe
    /// stop ids, and re-dates entries onto the query date with a post-midnight rollover. The directory is passed in,
    /// so these tests never touch the environment variable.
    /// </summary>
    public sealed class TripFileCacheTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "awtrixsharp-cache-tests-" + Guid.NewGuid());
        private readonly Mock<ILogger> _logger = new();

        public TripFileCacheTests()
        {
            Directory.CreateDirectory(_directory);
        }

        public void Dispose()
        {
            Directory.Delete(_directory, recursive: true);
        }

        private TripFileCache Cache() => new(_directory, _logger.Object);

        private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

        private void WriteTrips(string fileName, params (string Departs, string Arrives, string Place)[] trips)
        {
            var summaries = trips.Select(t => new TripSummary
            {
                Origin = TimePlace.Factory(At(t.Departs), t.Place),
                Destination = TimePlace.Factory(At(t.Arrives), "Destination")
            }).ToList();
            File.WriteAllText(Path.Combine(_directory, fileName), JsonSerializer.Serialize(summaries));
        }

        private void VerifyWarned() =>
            _logger.Verify(x => x.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public async Task NoDirectory_ReturnsNull(string? directory)
        {
            Assert.Null(await new TripFileCache(directory, _logger.Object).TryLoadAsync("1", "2", At("2025-01-01T08:00:00+11:00")));
        }

        [Fact]
        public async Task MissingFile_ReturnsNull()
        {
            Assert.Null(await Cache().TryLoadAsync("1", "2", At("2025-01-01T08:00:00+11:00")));
        }

        [Fact]
        public async Task SameDayEntry_IsRedatedOntoTheQueryDate_KeepingWallClockSecondsAndPlace()
        {
            WriteTrips("trip_200080_200060_08.json", ("2000-01-01T08:30:15+11:00", "2000-01-01T09:00:45+11:00", "Central"));

            var trips = await Cache().TryLoadAsync("200080", "200060", At("2025-01-01T08:00:00+11:00"));

            var trip = Assert.Single(trips!);
            Assert.Equal(At("2025-01-01T08:30:15+11:00"), trip.Origin.Time);
            Assert.Equal(TimeSpan.FromHours(11), trip.Origin.Time.Offset);
            Assert.Equal("Central", trip.Origin.Place);
            Assert.Equal(At("2025-01-01T09:00:45+11:00"), trip.Destination.Time);
        }

        [Fact]
        public async Task HourKey_IsTheSydneyHour_WhateverTheCallersOffset()
        {
            WriteTrips("trip_1_2_08.json", ("2000-01-01T08:45:00+11:00", "2000-01-01T09:15:00+11:00", "Central"));

            var trips = await Cache().TryLoadAsync("1", "2", At("2024-12-31T21:30:00Z")); // 08:30 AEDT on 1 January

            Assert.Equal(At("2025-01-01T08:45:00+11:00"), Assert.Single(trips!).Origin.Time);
        }

        [Fact]
        public async Task EntriesAfterMidnight_RollForwardADay()
        {
            WriteTrips("trip_1_2_23.json",
                ("2000-01-01T23:00:00+11:00", "2000-01-01T23:20:00+11:00", "WithinTolerance"),
                ("2000-01-01T23:45:00+11:00", "2000-01-01T23:59:00+11:00", "SameDay"),
                ("2000-01-01T23:50:00+11:00", "2000-01-02T00:20:00+11:00", "ArrivesTomorrow"),
                ("2000-01-02T00:10:00+11:00", "2000-01-02T00:40:00+11:00", "Tomorrow"));

            var trips = (await Cache().TryLoadAsync("1", "2", At("2025-01-01T23:30:00+11:00")))!;

            Assert.Equal(At("2025-01-01T23:00:00+11:00"), trips.Single(t => t.Origin.Place == "WithinTolerance").Origin.Time);
            Assert.Equal(At("2025-01-01T23:45:00+11:00"), trips.Single(t => t.Origin.Place == "SameDay").Origin.Time);
            Assert.Equal(At("2025-01-02T00:20:00+11:00"), trips.Single(t => t.Origin.Place == "ArrivesTomorrow").Destination.Time);
            Assert.Equal(At("2025-01-02T00:10:00+11:00"), trips.Single(t => t.Origin.Place == "Tomorrow").Origin.Time);
        }

        [Fact]
        public async Task EntryOnADstStartDate_GetsThatDatesOffset()
        {
            WriteTrips("trip_1_2_01.json", ("2000-01-01T03:10:00+10:00", "2000-01-01T03:40:00+10:00", "AfterSpringForward"));

            var trips = await Cache().TryLoadAsync("1", "2", At("2024-10-06T01:30:00+10:00"));

            var trip = Assert.Single(trips!);
            Assert.Equal(At("2024-10-06T03:10:00+11:00"), trip.Origin.Time);
            Assert.Equal(TimeSpan.FromHours(11), trip.Origin.Time.Offset);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("[null]")]
        public async Task UnusableFile_ReturnsNull_AndWarns(string content)
        {
            File.WriteAllText(Path.Combine(_directory, "trip_1_2_08.json"), content);

            Assert.Null(await Cache().TryLoadAsync("1", "2", At("2025-01-01T08:00:00+11:00")));
            VerifyWarned();
        }

        [Theory]
        [InlineData("../secret", "2")]
        [InlineData("1", "a/b")]
        [InlineData("1", "a\\b")]
        [InlineData("", "2")]
        public async Task UnsafeStopIds_ReturnNull(string origin, string destination)
        {
            Assert.Null(await Cache().TryLoadAsync(origin, destination, At("2025-01-01T08:00:00+11:00")));
        }

        [Fact]
        public async Task StopIdWithATrailingNewline_IsRejectedByTheAllowList()
        {
            // WS6 review m5: .NET "$" also matches before a final newline; the allow-list must reject it, not just miss the file
            Assert.Null(await Cache().TryLoadAsync("123\n", "2", At("2025-01-01T08:00:00+11:00")));

            _logger.Verify(x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("may only contain")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
    }
}
