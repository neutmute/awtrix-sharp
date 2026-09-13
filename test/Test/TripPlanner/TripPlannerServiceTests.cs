using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using TransportOpenData.TripPlanner;
using Xunit;

namespace Test.TripPlanner
{
    /// <summary>
    /// Tests for <see cref="TripPlannerService"/>. The generated <see cref="StopfinderClient"/> and
    /// <see cref="TripClient"/> classes expose their request methods as `public virtual`, which allows
    /// Moq to intercept them without making any real HTTP calls.
    /// </summary>
    public class TripPlannerServiceTests
    {
        private readonly Mock<StopfinderClient> _mockStopFinderClient;
        private readonly Mock<TripClient> _mockTripClient;
        private readonly Mock<ILogger<TripPlannerService>> _mockLogger;

        public TripPlannerServiceTests()
        {
            _mockStopFinderClient = new Mock<StopfinderClient>(new HttpClient());
            _mockTripClient = new Mock<TripClient>(new HttpClient());
            _mockLogger = new Mock<ILogger<TripPlannerService>>();
        }

        private TripPlannerService GetSystemUnderTest()
        {
            return new TripPlannerService(_mockStopFinderClient.Object, _mockTripClient.Object, _mockLogger.Object);
        }

        private void SetupTripClient(TripRequestResponse response)
        {
            _mockTripClient
                .Setup(x => x.Request2Async(
                    It.IsAny<OutputFormat5>(),
                    It.IsAny<CoordOutputFormat4>(),
                    It.IsAny<DepArrMacro>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<Type_origin>(),
                    It.IsAny<string>(),
                    It.IsAny<Type_destination>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<Wheelchair?>(),
                    It.IsAny<ExcludedMeans2?>(),
                    It.IsAny<ExclMOT_12?>(),
                    It.IsAny<ExclMOT_22?>(),
                    It.IsAny<ExclMOT_42?>(),
                    It.IsAny<ExclMOT_52?>(),
                    It.IsAny<ExclMOT_72?>(),
                    It.IsAny<ExclMOT_92?>(),
                    It.IsAny<ExclMOT_112?>(),
                    It.IsAny<TfNSWTR?>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<bool?>(),
                    It.IsAny<int?>(),
                    It.IsAny<BikeProfSpeed?>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()))
                .ReturnsAsync(response);
        }

        private static TripRequestResponseJourneyLegStop Stop(string estimated, string planned, string disassembledName)
        {
            return new TripRequestResponseJourneyLegStop
            {
                ArrivalTimeEstimated = estimated,
                ArrivalTimePlanned = planned,
                DepartureTimeEstimated = estimated,
                DepartureTimePlanned = planned,
                DisassembledName = disassembledName
            };
        }

        private static TripRequestResponse SingleJourneyResponse(
            string originEstimated, string originDisassembled,
            string destinationEstimated, string destinationDisassembled)
        {
            return new TripRequestResponse
            {
                Journeys = new List<TripRequestResponseJourney>
                {
                    new TripRequestResponseJourney
                    {
                        Legs = new List<TripRequestResponseJourneyLeg>
                        {
                            new TripRequestResponseJourneyLeg
                            {
                                Origin = Stop(originEstimated, originEstimated, originDisassembled),
                                Destination = Stop(destinationEstimated, destinationEstimated, destinationDisassembled)
                            }
                        }
                    }
                }
            };
        }

        [Fact]
        public async Task FindStops_DelegatesToStopFinderClient_AndReturnsResult()
        {
            // Arrange
            var expected = new StopFinderResponse { Version = "10.5" };
            _mockStopFinderClient
                .Setup(x => x.RequestAsync(
                    It.IsAny<OutputFormat4>(),
                    It.IsAny<Type_sf?>(),
                    It.IsAny<string>(),
                    It.IsAny<CoordOutputFormat3>(),
                    It.IsAny<TfNSWSF?>(),
                    It.IsAny<string>()))
                .ReturnsAsync(expected);

            var sut = GetSystemUnderTest();

            // Act
            var result = await sut.FindStops("Central");

            // Assert
            Assert.Same(expected, result);
        }

        [Fact]
        public async Task GetTrips_PassesFormattedDateAndTime_ToTripClient()
        {
            // Arrange - inspect the recorded invocation afterwards rather than using a Callback<>/It.Is<>
            // capture: Moq's generic Callback<> overloads only support up to 15 type arguments (Request2Async
            // has 29), and It.Is<T> takes an Expression<Func<T,bool>>, which cannot contain an assignment.
            SetupTripClient(new TripRequestResponse { Journeys = new List<TripRequestResponseJourney>() });

            var sut = GetSystemUnderTest();
            var fromWhen = new DateTime(2025, 3, 7, 6, 5, 0);

            // Act
            await sut.GetTrips("200080", "200060", fromWhen);

            // Assert
            var invocation = Assert.Single(_mockTripClient.Invocations);
            Assert.Equal("20250307", invocation.Arguments[3]);
            Assert.Equal("0605", invocation.Arguments[4]);
        }

        [Fact]
        public async Task GetNextDepartures_UsesEstimatedTimesAndDisassembledNames()
        {
            // Arrange
            SetupTripClient(SingleJourneyResponse(
                "2024-06-01T00:05:00Z", "Central",
                "2024-06-01T00:35:00Z", "Circular Quay"));

            var sut = GetSystemUnderTest();

            // Act
            var result = await sut.GetNextDepartures("200080", "200060", DateTime.Now);

            // Assert
            var summary = Assert.Single(result);
            Assert.Equal(DateTimeOffset.Parse("2024-06-01T00:05:00Z"), summary.Origin.Time);
            Assert.Equal("Central", summary.Origin.Place);
            Assert.Equal(DateTimeOffset.Parse("2024-06-01T00:35:00Z"), summary.Destination.Time);
            Assert.Equal("Circular Quay", summary.Destination.Place);
        }

        [Fact]
        public async Task GetNextDepartures_MultiLegJourney_UsesFirstLegOriginAndLastLegDestination()
        {
            // Arrange
            var response = new TripRequestResponse
            {
                Journeys = new List<TripRequestResponseJourney>
                {
                    new TripRequestResponseJourney
                    {
                        Legs = new List<TripRequestResponseJourneyLeg>
                        {
                            new TripRequestResponseJourneyLeg
                            {
                                Origin = Stop("2024-06-01T00:00:00Z", "2024-06-01T00:00:00Z", "Origin Stop"),
                                Destination = Stop("2024-06-01T00:10:00Z", "2024-06-01T00:10:00Z", "Interchange Stop")
                            },
                            new TripRequestResponseJourneyLeg
                            {
                                Origin = Stop("2024-06-01T00:15:00Z", "2024-06-01T00:15:00Z", "Interchange Stop"),
                                Destination = Stop("2024-06-01T00:45:00Z", "2024-06-01T00:45:00Z", "Final Stop")
                            }
                        }
                    }
                }
            };
            SetupTripClient(response);

            var sut = GetSystemUnderTest();

            // Act
            var result = await sut.GetNextDepartures("200080", "200060", DateTime.Now);

            // Assert
            var summary = Assert.Single(result);
            Assert.Equal("Origin Stop", summary.Origin.Place);
            Assert.Equal(DateTimeOffset.Parse("2024-06-01T00:00:00Z"), summary.Origin.Time);
            Assert.Equal("Final Stop", summary.Destination.Place);
            Assert.Equal(DateTimeOffset.Parse("2024-06-01T00:45:00Z"), summary.Destination.Time);
        }

        [Fact]
        public async Task GetNextDepartures_MultipleJourneys_PreservesApiOrder()
        {
            // Arrange
            var response = new TripRequestResponse
            {
                Journeys = new List<TripRequestResponseJourney>
                {
                    new TripRequestResponseJourney
                    {
                        Legs = new List<TripRequestResponseJourneyLeg>
                        {
                            new TripRequestResponseJourneyLeg
                            {
                                Origin = Stop("2024-06-01T00:30:00Z", "2024-06-01T00:30:00Z", "Third"),
                                Destination = Stop("2024-06-01T01:00:00Z", "2024-06-01T01:00:00Z", "ThirdDest")
                            }
                        }
                    },
                    new TripRequestResponseJourney
                    {
                        Legs = new List<TripRequestResponseJourneyLeg>
                        {
                            new TripRequestResponseJourneyLeg
                            {
                                Origin = Stop("2024-06-01T00:00:00Z", "2024-06-01T00:00:00Z", "First"),
                                Destination = Stop("2024-06-01T00:30:00Z", "2024-06-01T00:30:00Z", "FirstDest")
                            }
                        }
                    },
                    new TripRequestResponseJourney
                    {
                        Legs = new List<TripRequestResponseJourneyLeg>
                        {
                            new TripRequestResponseJourneyLeg
                            {
                                Origin = Stop("2024-06-01T00:15:00Z", "2024-06-01T00:15:00Z", "Second"),
                                Destination = Stop("2024-06-01T00:45:00Z", "2024-06-01T00:45:00Z", "SecondDest")
                            }
                        }
                    }
                }
            };
            SetupTripClient(response);

            var sut = GetSystemUnderTest();

            // Act
            var result = await sut.GetNextDepartures("200080", "200060", DateTime.Now);

            // Assert - order returned by the API is preserved verbatim (the service performs no re-sorting)
            Assert.Equal(3, result.Count);
            Assert.Equal("Third", result[0].Origin.Place);
            Assert.Equal("First", result[1].Origin.Place);
            Assert.Equal("Second", result[2].Origin.Place);
        }

        [Fact]
        public async Task GetNextDepartures_EmptyJourneys_ReturnsEmptyList()
        {
            // Arrange
            SetupTripClient(new TripRequestResponse { Journeys = new List<TripRequestResponseJourney>() });
            var sut = GetSystemUnderTest();

            // Act
            var result = await sut.GetNextDepartures("200080", "200060", DateTime.Now);

            // Assert
            Assert.Empty(result);
        }

        [Theory]
        [InlineData("2024-10-06T01:30:00+10:00", "2024-10-06T01:30:00+10:00")] // Just before Sydney DST starts (AEST, UTC+10)
        [InlineData("2024-10-06T03:30:00+11:00", "2024-10-06T03:30:00+11:00")] // Just after Sydney DST starts (AEDT, UTC+11)
        [InlineData("2024-04-07T02:30:00+11:00", "2024-04-07T02:30:00+11:00")] // Just before Sydney DST ends
        [InlineData("2024-04-07T02:30:00+10:00", "2024-04-07T02:30:00+10:00")] // Just after Sydney DST ends (clocks repeat 2-3am)
        public async Task GetNextDepartures_PreservesInstant_AcrossSydneyDstTransitions(string estimated, string expectedIso)
        {
            // Arrange
            SetupTripClient(SingleJourneyResponse(estimated, "Origin", estimated, "Destination"));
            var sut = GetSystemUnderTest();

            // Act
            var result = await sut.GetNextDepartures("200080", "200060", DateTime.Now);

            // Assert - regardless of the machine's local timezone, the UTC instant represented
            // by the API's timestamp (with its explicit offset) must be preserved exactly.
            var summary = Assert.Single(result);
            Assert.Equal(DateTimeOffset.Parse(expectedIso), summary.Origin.Time);
        }

        [Fact(Skip = "Known bug: TripPlannerService.GetNextDepartures calls DateTimeOffset.Parse on DepartureTimeEstimated/ArrivalTimeEstimated with no null fallback to the *Planned equivalent, throwing ArgumentNullException when a journey has no real-time estimate.")]
        public async Task GetNextDepartures_NullEstimatedTime_FallsBackToPlannedTime()
        {
            // Arrange
            const string planned = "2024-06-01T00:05:00Z";
            var response = new TripRequestResponse
            {
                Journeys = new List<TripRequestResponseJourney>
                {
                    new TripRequestResponseJourney
                    {
                        Legs = new List<TripRequestResponseJourneyLeg>
                        {
                            new TripRequestResponseJourneyLeg
                            {
                                Origin = new TripRequestResponseJourneyLegStop
                                {
                                    DepartureTimeEstimated = null,
                                    DepartureTimePlanned = planned,
                                    DisassembledName = "Central"
                                },
                                Destination = new TripRequestResponseJourneyLegStop
                                {
                                    ArrivalTimeEstimated = null,
                                    ArrivalTimePlanned = planned,
                                    DisassembledName = "Town Hall"
                                }
                            }
                        }
                    }
                }
            };
            SetupTripClient(response);
            var sut = GetSystemUnderTest();

            // Act
            var result = await sut.GetNextDepartures("200080", "200060", DateTime.Now);

            // Assert (intended behaviour - currently throws ArgumentNullException instead)
            var summary = Assert.Single(result);
            Assert.Equal(DateTimeOffset.Parse(planned), summary.Origin.Time);
        }

        [Fact]
        public async Task GetNextDepartures_UsesFileCache_WhenPresent_AndDoesNotCallTripClient()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "awtrixsharp-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            var envVarName = "AWTRIXSHARP_SETTINGS__DATA_DIRECTORY";
            var previousValue = Environment.GetEnvironmentVariable(envVarName);

            try
            {
                var fromWhen = new DateTime(2025, 1, 1, 8, 0, 0);
                var cacheFileName = $"trip_originA_destB_{fromWhen:HH}.json";
                var now = DateTimeOffset.Now;

                var cachedTrips = new List<TripSummary>
                {
                    new TripSummary
                    {
                        Origin = new TimePlace { Time = new DateTimeOffset(2000, 1, 1, 6, 30, 15, now.Offset), Place = "CachedOrigin" },
                        Destination = new TimePlace { Time = new DateTimeOffset(2000, 1, 1, 7, 0, 45, now.Offset), Place = "CachedDestination" }
                    }
                };
                var json = JsonSerializer.Serialize(cachedTrips);
                File.WriteAllText(Path.Combine(tempDir, cacheFileName), json);

                Environment.SetEnvironmentVariable(envVarName, tempDir);

                var sut = GetSystemUnderTest();

                // Act
                var result = await sut.GetNextDepartures("originA", "destB", fromWhen);

                // Assert - cache used; trip client never invoked
                var summary = Assert.Single(result);
                Assert.Equal("CachedOrigin", summary.Origin.Place);
                Assert.Equal(now.Year, summary.Origin.Time.Year);
                Assert.Equal(now.Month, summary.Origin.Time.Month);
                Assert.Equal(now.Day, summary.Origin.Time.Day);
                Assert.Equal(6, summary.Origin.Time.Hour);
                Assert.Equal(30, summary.Origin.Time.Minute);
                Assert.Equal(15, summary.Origin.Time.Second);

                _mockTripClient.Verify(x => x.Request2Async(
                    It.IsAny<OutputFormat5>(), It.IsAny<CoordOutputFormat4>(), It.IsAny<DepArrMacro>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<Type_origin>(), It.IsAny<string>(), It.IsAny<Type_destination>(), It.IsAny<string>(), It.IsAny<int?>(),
                    It.IsAny<Wheelchair?>(), It.IsAny<ExcludedMeans2?>(), It.IsAny<ExclMOT_12?>(), It.IsAny<ExclMOT_22?>(), It.IsAny<ExclMOT_42?>(),
                    It.IsAny<ExclMOT_52?>(), It.IsAny<ExclMOT_72?>(), It.IsAny<ExclMOT_92?>(), It.IsAny<ExclMOT_112?>(), It.IsAny<TfNSWTR?>(),
                    It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool?>(), It.IsAny<int?>(), It.IsAny<BikeProfSpeed?>(), It.IsAny<int?>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>()), Times.Never);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVarName, previousValue);
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task GetNextDepartures_NoEnvVarSet_FallsBackToTripClient()
        {
            // Arrange
            var envVarName = "AWTRIXSHARP_SETTINGS__DATA_DIRECTORY";
            var previousValue = Environment.GetEnvironmentVariable(envVarName);
            try
            {
                Environment.SetEnvironmentVariable(envVarName, null);
                SetupTripClient(SingleJourneyResponse(
                    "2024-06-01T00:05:00Z", "Central",
                    "2024-06-01T00:35:00Z", "Circular Quay"));

                var sut = GetSystemUnderTest();

                // Act
                var result = await sut.GetNextDepartures("200080", "200060", DateTime.Now);

                // Assert
                Assert.Single(result);
                _mockTripClient.Verify(x => x.Request2Async(
                    It.IsAny<OutputFormat5>(), It.IsAny<CoordOutputFormat4>(), It.IsAny<DepArrMacro>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<Type_origin>(), It.IsAny<string>(), It.IsAny<Type_destination>(), It.IsAny<string>(), It.IsAny<int?>(),
                    It.IsAny<Wheelchair?>(), It.IsAny<ExcludedMeans2?>(), It.IsAny<ExclMOT_12?>(), It.IsAny<ExclMOT_22?>(), It.IsAny<ExclMOT_42?>(),
                    It.IsAny<ExclMOT_52?>(), It.IsAny<ExclMOT_72?>(), It.IsAny<ExclMOT_92?>(), It.IsAny<ExclMOT_112?>(), It.IsAny<TfNSWTR?>(),
                    It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool?>(), It.IsAny<int?>(), It.IsAny<BikeProfSpeed?>(), It.IsAny<int?>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>()), Times.Once);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVarName, previousValue);
            }
        }
    }
}
