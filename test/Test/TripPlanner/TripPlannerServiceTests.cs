using System.Globalization;
using System.Net;
using System.Text.Json;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TransportOpenData;
using TransportOpenData.TripPlanner;
using static Test.TripPlanner.TripPlannerTestData;

namespace Test.TripPlanner
{
    /// <summary>
    /// TripPlannerService over the real generated clients and a stub HttpMessageHandler (CR-40): production serializer
    /// settings are exercised and no network is used. Tests that set AWTRIXSHARP_SETTINGS__DATA_DIRECTORY live in
    /// this class so xUnit runs them serially.
    /// </summary>
    public class TripPlannerServiceTests
    {
        private const string DataDirectoryVariable = "AWTRIXSHARP_SETTINGS__DATA_DIRECTORY";
        private static readonly DateTimeOffset AnyTime = new(2024, 6, 1, 9, 0, 0, TimeSpan.FromHours(10));

        private static (TripPlannerService Service, StubHttpMessageHandler Handler) ServiceReturning(TripRequestResponse response)
        {
            var handler = StubHttpMessageHandler.Json(ToJson(response));
            return (CreateService(handler), handler);
        }

        [Fact]
        public async Task FindStops_UsesTheConfiguredBaseUrl_AndReturnsTheResponse()
        {
            var handler = StubHttpMessageHandler.Json("{\"version\":\"10.5\"}");
            var sut = CreateService(handler);

            var result = await sut.FindStops("Central");

            Assert.Equal("10.5", result.Version);
            Assert.StartsWith(BaseUrl + "/stop_finder?", Assert.Single(handler.RequestUris).ToString());
        }

        [Fact]
        public async Task GetTrips_BlankBaseUrl_UsesTheGeneratedDefault()
        {
            var handler = StubHttpMessageHandler.Json("{\"journeys\":[]}");
            var sut = CreateService(handler, baseUrl: "");

            await sut.GetTrips("200080", "200060", AnyTime);

            Assert.StartsWith("https://api.transport.nsw.gov.au/v1/tp/trip?", Assert.Single(handler.RequestUris).ToString());
        }

        [Fact]
        public async Task GetTrips_AtSixTwentyTwoSydneyFromAUtcHost_QueriesTheSydneyDateAndTime()
        {
            // CR-26: a UTC container used to ask for 20:22 on the previous date
            var (sut, handler) = ServiceReturning(Response());

            await sut.GetTrips("200080", "200060", DateTimeOffset.Parse("2025-08-15T20:22:00Z", CultureInfo.InvariantCulture));

            var uri = Assert.Single(handler.RequestUris).ToString();
            Assert.Contains("itdDate=20250816", uri);
            Assert.Contains("itdTime=0622", uri);
        }

        [Fact]
        public async Task EachCall_CreatesAFreshNamedClient()
        {
            // CR-29: nothing captures an HttpClient for the life of the process
            var factory = new StubHttpClientFactory(StubHttpMessageHandler.Json("{\"journeys\":[]}"));
            var sut = new TripPlannerService(factory, Options.Create(new TransportOpenDataConfig { BaseUrl = BaseUrl }), NullLogger<TripPlannerService>.Instance);

            await sut.GetTrips("200080", "200060", AnyTime);
            await sut.GetTrips("200080", "200060", AnyTime);

            Assert.Equal(new[] { TripPlannerService.HttpClientName, TripPlannerService.HttpClientName }, factory.CreatedNames);
        }

        [Fact]
        public async Task GetNextDepartures_CancellingTheToken_CancelsTheRequest()
        {
            var handler = new StubHttpMessageHandler((_, token) =>
            {
                var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                token.Register(() => pending.TrySetCanceled(token));
                return pending.Task;
            });
            var sut = CreateService(handler);
            using var cts = new CancellationTokenSource();

            var call = sut.GetNextDepartures("200080", "200060", AnyTime, cts.Token);
            Assert.False(call.IsCompleted);
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        private static (StalledStream Body, StubHttpMessageHandler Handler) StalledBody()
        {
            var body = new StalledStream();
            var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(body)
            }));
            return (body, handler);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task StalledResponseBody_TimesOutAfterHttpTimeout_AsAnOrdinaryFailure(bool stopFinder)
        {
            // WS6 review I1: HttpClient.Timeout ends at the headers; the body read must be bounded too
            var (body, handler) = StalledBody();
            var time = new FakeTimeProvider(AnyTime);
            var sut = CreateService(handler);
            sut.TimeProvider = time;

            Task call = stopFinder ? sut.FindStops("Central") : sut.GetNextDepartures("200080", "200060", AnyTime);
            await body.ReadStarted.WaitAsync(TimeSpan.FromSeconds(5));

            time.Advance(TripPlannerService.HttpTimeout - TimeSpan.FromSeconds(1));
            Assert.False(call.IsCompleted);

            time.Advance(TimeSpan.FromSeconds(1));
            var ex = await Assert.ThrowsAsync<TimeoutException>(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsAssignableFrom<OperationCanceledException>(ex.InnerException);
        }

        [Fact]
        public async Task CancellingTheCallersToken_DuringAStalledBody_IsStillACancellation()
        {
            var (body, handler) = StalledBody();
            var sut = CreateService(handler);
            sut.TimeProvider = new FakeTimeProvider(AnyTime);
            using var cts = new CancellationTokenSource();

            var call = sut.GetNextDepartures("200080", "200060", AnyTime, cts.Token);
            await body.ReadStarted.WaitAsync(TimeSpan.FromSeconds(5));
            cts.Cancel();

            var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsNotType<TimeoutException>(ex);
        }

        [Fact]
        public async Task GetNextDepartures_UsesEstimatedTimesAndDisassembledNames()
        {
            var (sut, _) = ServiceReturning(Response(Journey(Leg(
                Stop("2024-06-01T00:05:00Z", "2024-06-01T00:07:00Z", "Central"),
                Stop("2024-06-01T00:35:00Z", "2024-06-01T00:37:00Z", "Circular Quay")))));

            var result = await sut.GetNextDepartures("200080", "200060", AnyTime);

            var summary = Assert.Single(result);
            Assert.Equal(DateTimeOffset.Parse("2024-06-01T00:05:00Z", CultureInfo.InvariantCulture), summary.Origin.Time);
            Assert.Equal("Central", summary.Origin.Place);
            Assert.Equal(DateTimeOffset.Parse("2024-06-01T00:35:00Z", CultureInfo.InvariantCulture), summary.Destination.Time);
            Assert.Equal("Circular Quay", summary.Destination.Place);
        }

        [Fact]
        public async Task GetNextDepartures_ReturnsTimesWithTheSydneyOffset()
        {
            var (sut, _) = ServiceReturning(Response(Journey(Leg(
                Stop("2024-06-01T00:05:00Z", null, "Central"),
                Stop("2024-06-01T00:35:00Z", null, "Circular Quay")))));

            var summary = Assert.Single(await sut.GetNextDepartures("200080", "200060", AnyTime));

            Assert.Equal(TimeSpan.FromHours(10), summary.Origin.Time.Offset);
            Assert.Equal(10, summary.Origin.Time.Hour);
        }

        [Fact]
        public async Task GetNextDepartures_MultiLegJourney_UsesFirstLegOriginAndLastLegDestination()
        {
            var (sut, _) = ServiceReturning(Response(Journey(
                Leg(Stop("2024-06-01T00:00:00Z", "2024-06-01T00:00:00Z", "Origin Stop"), Stop("2024-06-01T00:10:00Z", "2024-06-01T00:10:00Z", "Interchange Stop")),
                Leg(Stop("2024-06-01T00:15:00Z", "2024-06-01T00:15:00Z", "Interchange Stop"), Stop("2024-06-01T00:45:00Z", "2024-06-01T00:45:00Z", "Final Stop")))));

            var summary = Assert.Single(await sut.GetNextDepartures("200080", "200060", AnyTime));

            Assert.Equal("Origin Stop", summary.Origin.Place);
            Assert.Equal(DateTimeOffset.Parse("2024-06-01T00:00:00Z", CultureInfo.InvariantCulture), summary.Origin.Time);
            Assert.Equal("Final Stop", summary.Destination.Place);
            Assert.Equal(DateTimeOffset.Parse("2024-06-01T00:45:00Z", CultureInfo.InvariantCulture), summary.Destination.Time);
        }

        [Fact]
        public async Task GetNextDepartures_MultipleJourneys_PreservesApiOrder()
        {
            var (sut, _) = ServiceReturning(Response(
                Journey(Leg(Stop("2024-06-01T00:30:00Z", null, "Third"), Stop("2024-06-01T01:00:00Z", null, "ThirdDest"))),
                Journey(Leg(Stop("2024-06-01T00:00:00Z", null, "First"), Stop("2024-06-01T00:30:00Z", null, "FirstDest"))),
                Journey(Leg(Stop("2024-06-01T00:15:00Z", null, "Second"), Stop("2024-06-01T00:45:00Z", null, "SecondDest")))));

            var result = await sut.GetNextDepartures("200080", "200060", AnyTime);

            Assert.Equal(new[] { "Third", "First", "Second" }, result.Select(r => r.Origin.Place));
        }

        [Fact]
        public async Task GetNextDepartures_EmptyJourneys_ReturnsEmptyList()
        {
            var (sut, _) = ServiceReturning(Response());

            Assert.Empty(await sut.GetNextDepartures("200080", "200060", AnyTime));
        }

        [Theory]
        [InlineData("2024-10-06T01:30:00+10:00")] // just before Sydney DST starts (AEST)
        [InlineData("2024-10-06T03:30:00+11:00")] // just after Sydney DST starts (AEDT)
        [InlineData("2024-04-07T02:30:00+11:00")] // just before Sydney DST ends
        [InlineData("2024-04-07T02:30:00+10:00")] // just after Sydney DST ends (clocks repeat 2-3am)
        public async Task GetNextDepartures_PreservesInstantAndSydneyOffset_AcrossDstTransitions(string estimated)
        {
            var (sut, _) = ServiceReturning(Response(Journey(Leg(Stop(estimated, estimated, "Origin"), Stop(estimated, estimated, "Destination")))));

            var summary = Assert.Single(await sut.GetNextDepartures("200080", "200060", AnyTime));

            var expected = DateTimeOffset.Parse(estimated, CultureInfo.InvariantCulture);
            Assert.Equal(expected, summary.Origin.Time);
            Assert.Equal(expected.Offset, summary.Origin.Time.Offset);
        }

        [Fact]
        public async Task GetNextDepartures_NullEstimatedTime_FallsBackToPlannedTime()
        {
            const string planned = "2024-06-01T00:05:00Z";
            var (sut, _) = ServiceReturning(Response(Journey(Leg(
                new TripRequestResponseJourneyLegStop { DepartureTimeEstimated = null, DepartureTimePlanned = planned, DisassembledName = "Central" },
                new TripRequestResponseJourneyLegStop { ArrivalTimeEstimated = null, ArrivalTimePlanned = planned, DisassembledName = "Town Hall" }))));

            var summary = Assert.Single(await sut.GetNextDepartures("200080", "200060", AnyTime));

            Assert.Equal(DateTimeOffset.Parse(planned, CultureInfo.InvariantCulture), summary.Origin.Time);
        }

        [Fact]
        public async Task GetNextDepartures_UsesFileCache_WhenPresent_AndDoesNotCallTheApi()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "awtrixsharp-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            var previousValue = Environment.GetEnvironmentVariable(DataDirectoryVariable);

            try
            {
                var fromWhen = new DateTimeOffset(2025, 1, 1, 8, 0, 0, TimeSpan.FromHours(11)); // 08:00 Sydney (AEDT)
                var cachedTrips = new List<TripSummary>
                {
                    new()
                    {
                        Origin = new TimePlace { Time = new DateTimeOffset(2000, 1, 1, 8, 30, 15, TimeSpan.FromHours(11)), Place = "CachedOrigin" },
                        Destination = new TimePlace { Time = new DateTimeOffset(2000, 1, 1, 9, 0, 45, TimeSpan.FromHours(11)), Place = "CachedDestination" }
                    }
                };
                File.WriteAllText(Path.Combine(tempDir, "trip_originA_destB_08.json"), JsonSerializer.Serialize(cachedTrips));
                Environment.SetEnvironmentVariable(DataDirectoryVariable, tempDir);

                var handler = StubHttpMessageHandler.Json("{\"journeys\":[]}");
                var sut = CreateService(handler);

                var result = await sut.GetNextDepartures("originA", "destB", fromWhen);

                var summary = Assert.Single(result);
                Assert.Equal("CachedOrigin", summary.Origin.Place);
                Assert.Equal(new DateTimeOffset(2025, 1, 1, 8, 30, 15, TimeSpan.FromHours(11)), summary.Origin.Time); // the query's date, not today's
                Assert.Empty(handler.RequestUris);
            }
            finally
            {
                Environment.SetEnvironmentVariable(DataDirectoryVariable, previousValue);
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task GetNextDepartures_UnreadableCacheFile_FallsBackToTheApi()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "awtrixsharp-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            var previousValue = Environment.GetEnvironmentVariable(DataDirectoryVariable);

            try
            {
                File.WriteAllText(Path.Combine(tempDir, "trip_200080_200060_09.json"), "{ not json");
                Environment.SetEnvironmentVariable(DataDirectoryVariable, tempDir);
                var (sut, handler) = ServiceReturning(Response(Journey(Leg(
                    Stop("2024-06-01T00:05:00Z", null, "Central"),
                    Stop("2024-06-01T00:35:00Z", null, "Circular Quay")))));

                var result = await sut.GetNextDepartures("200080", "200060", AnyTime); // 09:00 Sydney

                Assert.Equal("Central", Assert.Single(result).Origin.Place);
                Assert.Single(handler.RequestUris);
            }
            finally
            {
                Environment.SetEnvironmentVariable(DataDirectoryVariable, previousValue);
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task GetNextDepartures_NoDataDirectory_CallsTheApi()
        {
            var previousValue = Environment.GetEnvironmentVariable(DataDirectoryVariable);
            try
            {
                Environment.SetEnvironmentVariable(DataDirectoryVariable, null);
                var (sut, handler) = ServiceReturning(Response(Journey(Leg(
                    Stop("2024-06-01T00:05:00Z", null, "Central"),
                    Stop("2024-06-01T00:35:00Z", null, "Circular Quay")))));

                var result = await sut.GetNextDepartures("200080", "200060", AnyTime);

                Assert.Single(result);
                Assert.Single(handler.RequestUris);
            }
            finally
            {
                Environment.SetEnvironmentVariable(DataDirectoryVariable, previousValue);
            }
        }
    }
}
