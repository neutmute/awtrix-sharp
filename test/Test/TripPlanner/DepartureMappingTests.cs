using System.Globalization;
using System.Net;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Logging;
using Moq;
using TransportOpenData.TripPlanner;
using static Test.TripPlanner.TripPlannerTestData;

namespace Test.TripPlanner
{
    /// <summary>
    /// GetNextDepartures over real fixtures and hand-built responses: tolerant of error bodies and gaps (CR-10), boards the
    /// first transit leg rather than a leading walk and de-duplicates (CR-27), skips cancelled services (CR-28).
    /// </summary>
    public class DepartureMappingTests
    {
        private static readonly DateTimeOffset FromWhen = DateTimeOffset.Parse("2025-08-16T15:30:00+10:00", CultureInfo.InvariantCulture);
        private readonly Mock<ILogger<TripPlannerService>> _logger = new();

        private Task<List<TripSummary>> DeparturesFor(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            CreateService(StubHttpMessageHandler.Json(json, status), _logger.Object).GetNextDepartures("222316", "200070", FromWhen);

        private Task<List<TripSummary>> DeparturesFor(TripRequestResponse response) => DeparturesFor(ToJson(response));

        private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

        private static TripRequestResponseJourney GoodJourney(string departs = "2025-08-16T05:53:00Z", string place = "Oatley") =>
            Journey(Leg(Stop(departs, null, place), Stop("2025-08-16T06:40:00Z", null, "Central"), productClass: 5, "MONITORED"));

        private void VerifyWarning(string containing) =>
            _logger.Verify(x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(containing)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);

        [Fact]
        public async Task ErrorBody_ReturnsNoDepartures_AndLogsTheApiMessage()
        {
            var result = await DeparturesFor(Fixture("ErrorResponse.json"));

            Assert.Empty(result);
            VerifyWarning("not been authenticated");
        }

        [Fact]
        public async Task HttpError_StillThrows_SoTheCallerKeepsItsLastGoodList()
        {
            await Assert.ThrowsAnyAsync<TripPlannerException>(() => DeparturesFor("{}", HttpStatusCode.ServiceUnavailable));
        }

        [Fact]
        public async Task ComplexTripResponse_BoardsTheFirstTransitLeg_SkippingLeadingWalks_AndDeduplicates()
        {
            var result = await DeparturesFor(Fixture("ComplexTripResponse.json"));

            // J2 and J6 start with a class-100 walk (05:41:30Z, 06:04Z); J4 and J8 repeat J3 and J7's departures
            Assert.Equal(
                new[] { "15:37:54", "16:00:30", "15:53:00", "16:03:00", "16:23:00", "16:13:00" },
                result.Select(d => d.Origin.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)));
            Assert.All(result, d => Assert.Equal(TimeSpan.FromHours(10), d.Origin.Time.Offset));
            Assert.DoesNotContain(result, d => d.Origin.Time == At("2025-08-16T05:41:30Z"));
            Assert.Equal("Macquarie Pl at The Strand", result[1].Origin.Place);
            Assert.Equal("Town Hall Station, Platform 3", result[0].Destination.Place);
        }

        [Fact]
        public async Task SuccessfulTripResponse_FallsBackToStopSequenceTimes()
        {
            // The only leg's origin and destination carry no times; stopSequence does
            var summary = Assert.Single(await DeparturesFor(Fixture("SuccessfulTripResponse.json")));

            Assert.Equal(At("2023-06-01T12:01:00Z"), summary.Origin.Time);
            Assert.Equal("Central", summary.Origin.Place);
            Assert.Equal(At("2023-06-01T12:11:00Z"), summary.Destination.Time);
            Assert.Equal("Circular Quay", summary.Destination.Place);
        }

        [Fact]
        public async Task MissingOrUnparsableEstimate_FallsBackToPlanned()
        {
            var summary = Assert.Single(await DeparturesFor(Response(Journey(Leg(
                Stop("soon", "2025-08-16T05:53:00Z", "Oatley"),
                Stop(null, "2025-08-16T06:40:00Z", "Central"))))));

            Assert.Equal(At("2025-08-16T05:53:00Z"), summary.Origin.Time);
            Assert.Equal(At("2025-08-16T06:40:00Z"), summary.Destination.Time);
        }

        [Fact]
        public async Task JourneyWithoutAnyDepartureTime_IsSkipped_AndTheOthersAreKept()
        {
            var result = await DeparturesFor(Response(
                Journey(Leg(Stop(null, null, "NoTimes"), Stop(null, null, "Nowhere"))),
                GoodJourney()));

            Assert.Equal("Oatley", Assert.Single(result).Origin.Place);
            VerifyWarning("no departure time");
        }

        [Fact]
        public async Task MissingArrival_UsesTheDepartureTime()
        {
            var summary = Assert.Single(await DeparturesFor(Response(Journey(Leg(
                Stop("2025-08-16T05:53:00Z", null, "Oatley"),
                Stop(null, null, "Central"))))));

            Assert.Equal(summary.Origin.Time, summary.Destination.Time);
        }

        [Fact]
        public async Task NullJourneys_NullLegsAndEmptyLegs_AreSkipped()
        {
            var result = await DeparturesFor(Response(
                null,
                new TripRequestResponseJourney { Legs = null },
                new TripRequestResponseJourney { Legs = new List<TripRequestResponseJourneyLeg>() },
                GoodJourney()));

            Assert.Equal("Oatley", Assert.Single(result).Origin.Place);
        }

        [Fact]
        public async Task WalkOnlyJourney_IsSkipped()
        {
            var result = await DeparturesFor(Response(
                Journey(Leg(Stop("2025-08-16T05:40:00Z", null, "Home"), Stop("2025-08-16T06:10:00Z", null, "Work"), productClass: 99)),
                GoodJourney()));

            Assert.Equal("Oatley", Assert.Single(result).Origin.Place);
        }

        [Theory]
        [InlineData("CANCELLED")]
        [InlineData("TRIP_CANCELLED")]
        [InlineData("cancelled")]
        public async Task JourneyWithACancelledService_IsSkipped(string status)
        {
            var result = await DeparturesFor(Response(
                Journey(
                    Leg(Stop("2025-08-16T05:30:00Z", null, "Home"), Stop("2025-08-16T05:40:00Z", null, "Oatley"), productClass: 100),
                    Leg(Stop("2025-08-16T05:45:00Z", null, "Oatley"), Stop("2025-08-16T06:30:00Z", null, "Central"), productClass: 1, status)),
                GoodJourney()));

            Assert.Equal(At("2025-08-16T05:53:00Z"), Assert.Single(result).Origin.Time);
        }

        [Fact]
        public async Task DuplicateDepartureInstants_KeepTheFirstJourney()
        {
            var result = await DeparturesFor(Response(
                GoodJourney(place: "First"),
                GoodJourney(place: "Second")));

            Assert.Equal("First", Assert.Single(result).Origin.Place);
        }
    }
}
