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
    /// GetNextDepartures over real fixtures and hand-built responses: tolerant of error bodies and gaps (CR-10), departs at the
    /// first leg (a leading walk included) and de-duplicates journeys boarding the same service (CR-27, C1 ruling), skips cancelled services (CR-28).
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
        public async Task ComplexTripResponse_DepartsAtTheFirstLeg_IncludingLeadingWalks_AndDeduplicatesTheSameService()
        {
            var result = await DeparturesFor(Fixture("ComplexTripResponse.json"));

            // C1 ruling, hand-computed from the fixture (UTC -> +10:00):
            // J1 bus from 222316 05:37:54; J2 walk from Oatley Station 05:41:30, boards the 945 at Macquarie Pl 06:00:30;
            // J3 bus from 222316 05:53; J4 the same 05:53 boarding as J3 (dropped); J5 bus 06:03; J6 walk 06:04, boards at
            // Macquarie Pl 06:23; J7 bus from 222316 06:13; J8 the same 06:13 boarding as J7 (dropped)
            Assert.Equal(
                new[] { "15:37:54", "15:41:30", "15:53:00", "16:03:00", "16:04:00", "16:13:00" },
                result.Select(d => d.Origin.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)));
            Assert.All(result, d => Assert.Equal(TimeSpan.FromHours(10), d.Origin.Time.Offset));
            Assert.DoesNotContain(result, d => d.Origin.Time == At("2025-08-16T06:00:30Z") || d.Origin.Time == At("2025-08-16T06:23:00Z"));
            Assert.Equal("Macquarie Pl at The Strand", result[1].Origin.Place); // where the first transit leg is boarded
            Assert.Equal("Macquarie Pl at The Strand", result[4].Origin.Place);
            Assert.Equal("Town Hall Station, Platform 3", result[0].Destination.Place);
        }

        [Fact]
        public async Task LeadingWalk_DepartsAtTheWalkStart_FallingBackToPlanned()
        {
            var summary = Assert.Single(await DeparturesFor(Response(Journey(
                Leg(Stop("soon", "2025-08-16T05:41:00Z", "Oatley Station"), Stop("2025-08-16T06:00:00Z", null, "Macquarie Pl"), productClass: 100),
                Leg(Stop("2025-08-16T06:00:30Z", null, "Macquarie Pl"), Stop("2025-08-16T06:40:00Z", null, "Central"), productClass: 5)))));

            Assert.Equal(At("2025-08-16T05:41:00Z"), summary.Origin.Time);
            Assert.Equal("Macquarie Pl", summary.Origin.Place);
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

        /// <summary>
        /// CR-27's symptom: a walk to the platform ahead of the same train gave an alarm ~11 minutes early plus a duplicate
        /// alarm. Journeys boarding the same service collapse to the one with the latest first-leg departure (least waiting).
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task JourneysBoardingTheSameService_KeepTheLatestFirstLegDeparture_InTheFirstPosition(bool walkFirst)
        {
            var walkToTheSameTrain = Journey(
                Leg(Stop("2025-08-16T05:42:00Z", null, "Oatley Station"), Stop("2025-08-16T05:50:00Z", null, "Oatley"), productClass: 100),
                Leg(Stop("2025-08-16T05:53:00Z", null, "Oatley"), Stop("2025-08-16T06:40:00Z", null, "Central"), productClass: 1));
            var direct = GoodJourney(departs: "2025-08-16T05:53:00Z", place: "Oatley");
            var later = GoodJourney(departs: "2025-08-16T06:03:00Z", place: "Oatley");

            var result = await DeparturesFor(walkFirst
                ? Response(walkToTheSameTrain, later, direct)
                : Response(direct, later, walkToTheSameTrain));

            Assert.Equal(new[] { At("2025-08-16T05:53:00Z"), At("2025-08-16T06:03:00Z") }, result.Select(d => d.Origin.Time));
            Assert.DoesNotContain(result, d => d.Origin.Time == At("2025-08-16T05:42:00Z"));
        }

        /// <summary>
        /// WS6 rereview: strengthens the lock-in above. Two walk-first journeys board the *same* service; the later
        /// walk-start journey ("B") arrives second in API order. The old rule de-duplicated on the boarding leg's own
        /// time (identical for both, since neither Origin.Time recorded the walk), so it kept whichever came first
        /// ("A") outright. The current rule departs from the walk and replaces in place on a later Origin.Time, so B
        /// must win and land in A's position.
        /// </summary>
        [Fact]
        public async Task JourneysBoardingTheSameService_WalkStartWinsAndReplacesInPlace_RegardlessOfApiOrder()
        {
            var walkA = Journey(
                Leg(Stop("2025-08-16T05:40:00Z", null, "Oatley Station"), Stop("2025-08-16T05:50:00Z", null, "Oatley"), productClass: 100),
                Leg(Stop("2025-08-16T05:53:00Z", null, "Oatley"), Stop("2025-08-16T06:40:00Z", null, "A"), productClass: 1));
            var walkB = Journey(
                Leg(Stop("2025-08-16T05:45:00Z", null, "Oatley Station"), Stop("2025-08-16T05:50:00Z", null, "Oatley"), productClass: 100),
                Leg(Stop("2025-08-16T05:53:00Z", null, "Oatley"), Stop("2025-08-16T06:40:00Z", null, "B"), productClass: 1));
            var later = GoodJourney(departs: "2025-08-16T06:03:00Z", place: "Oatley"); // a distinct, later service

            var result = await DeparturesFor(Response(walkA, later, walkB)); // B (the later walk) arrives last in API order

            Assert.Equal(new[] { At("2025-08-16T05:45:00Z"), At("2025-08-16T06:03:00Z") }, result.Select(d => d.Origin.Time));
            Assert.Equal("B", result[0].Destination.Place);
            Assert.DoesNotContain(result, d => d.Origin.Time == At("2025-08-16T05:40:00Z") || d.Origin.Time == At("2025-08-16T05:53:00Z"));
        }

        [Fact]
        public async Task SameBoardingStopAndTime_WithEqualDepartures_KeepTheFirstJourney()
        {
            var result = await DeparturesFor(Response(
                GoodJourney(place: "Oatley"),
                Journey(Leg(Stop("2025-08-16T05:53:00Z", null, "Oatley"), Stop("2025-08-16T07:00:00Z", null, "Elsewhere"), productClass: 5))));

            Assert.Equal("Central", Assert.Single(result).Destination.Place);
        }

        [Fact]
        public async Task SameDepartureInstant_FromDifferentBoardingStops_AreDistinctServices()
        {
            var result = await DeparturesFor(Response(
                GoodJourney(place: "Oatley"),
                GoodJourney(place: "Mortdale")));

            Assert.Equal(new[] { "Oatley", "Mortdale" }, result.Select(d => d.Origin.Place));
        }
    }
}
