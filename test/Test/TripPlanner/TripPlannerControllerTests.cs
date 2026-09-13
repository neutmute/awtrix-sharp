using AwtrixSharpWeb.Controllers;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using TransportOpenData.TripPlanner;
using Xunit;

namespace Test.TripPlanner
{
    /// <summary>
    /// TripPlannerController takes a concrete TripPlannerService (not an interface), so these tests
    /// build a real TripPlannerService backed by mocked (Moq-able, `public virtual`) generated
    /// StopfinderClient/TripClient instances rather than mocking the service itself.
    /// </summary>
    public class TripPlannerControllerTests
    {
        private readonly Mock<StopfinderClient> _mockStopFinderClient;
        private readonly Mock<TripClient> _mockTripClient;
        private readonly Mock<ILogger<TripPlannerService>> _mockServiceLogger;
        private readonly Mock<ILogger<TripPlannerController>> _mockControllerLogger;

        public TripPlannerControllerTests()
        {
            _mockStopFinderClient = new Mock<StopfinderClient>(new HttpClient());
            _mockTripClient = new Mock<TripClient>(new HttpClient());
            _mockServiceLogger = new Mock<ILogger<TripPlannerService>>();
            _mockControllerLogger = new Mock<ILogger<TripPlannerController>>();
        }

        private TripPlannerController GetSystemUnderTest()
        {
            var service = new TripPlannerService(_mockStopFinderClient.Object, _mockTripClient.Object, _mockServiceLogger.Object);
            return new TripPlannerController(service, _mockControllerLogger.Object);
        }

        private void SetupTripClientResult(TripRequestResponse response)
        {
            _mockTripClient
                .Setup(x => x.Request2Async(
                    It.IsAny<OutputFormat5>(), It.IsAny<CoordOutputFormat4>(), It.IsAny<DepArrMacro>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<Type_origin>(), It.IsAny<string>(), It.IsAny<Type_destination>(), It.IsAny<string>(), It.IsAny<int?>(),
                    It.IsAny<Wheelchair?>(), It.IsAny<ExcludedMeans2?>(), It.IsAny<ExclMOT_12?>(), It.IsAny<ExclMOT_22?>(), It.IsAny<ExclMOT_42?>(),
                    It.IsAny<ExclMOT_52?>(), It.IsAny<ExclMOT_72?>(), It.IsAny<ExclMOT_92?>(), It.IsAny<ExclMOT_112?>(), It.IsAny<TfNSWTR?>(),
                    It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool?>(), It.IsAny<int?>(), It.IsAny<BikeProfSpeed?>(), It.IsAny<int?>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>()))
                .ReturnsAsync(response);
        }

        private void SetupTripClientThrows(Exception exception)
        {
            _mockTripClient
                .Setup(x => x.Request2Async(
                    It.IsAny<OutputFormat5>(), It.IsAny<CoordOutputFormat4>(), It.IsAny<DepArrMacro>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<Type_origin>(), It.IsAny<string>(), It.IsAny<Type_destination>(), It.IsAny<string>(), It.IsAny<int?>(),
                    It.IsAny<Wheelchair?>(), It.IsAny<ExcludedMeans2?>(), It.IsAny<ExclMOT_12?>(), It.IsAny<ExclMOT_22?>(), It.IsAny<ExclMOT_42?>(),
                    It.IsAny<ExclMOT_52?>(), It.IsAny<ExclMOT_72?>(), It.IsAny<ExclMOT_92?>(), It.IsAny<ExclMOT_112?>(), It.IsAny<TfNSWTR?>(),
                    It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool?>(), It.IsAny<int?>(), It.IsAny<BikeProfSpeed?>(), It.IsAny<int?>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>()))
                .ThrowsAsync(exception);
        }

        [Fact]
        public async Task FindStops_ReturnsOk_WithServiceResult()
        {
            // Arrange
            var expected = new StopFinderResponse { Version = "10.5" };
            _mockStopFinderClient
                .Setup(x => x.RequestAsync(
                    It.IsAny<OutputFormat4>(), It.IsAny<Type_sf?>(), It.IsAny<string>(), It.IsAny<CoordOutputFormat3>(),
                    It.IsAny<TfNSWSF?>(), It.IsAny<string>()))
                .ReturnsAsync(expected);

            var sut = GetSystemUnderTest();

            // Act
            var result = await sut.FindStops("Central");

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Same(expected, okResult.Value);
        }

        [Fact]
        public async Task FindStops_ServiceThrows_Returns500()
        {
            // Arrange
            _mockStopFinderClient
                .Setup(x => x.RequestAsync(
                    It.IsAny<OutputFormat4>(), It.IsAny<Type_sf?>(), It.IsAny<string>(), It.IsAny<CoordOutputFormat3>(),
                    It.IsAny<TfNSWSF?>(), It.IsAny<string>()))
                .ThrowsAsync(new HttpRequestException("network error"));

            var sut = GetSystemUnderTest();

            // Act
            var result = await sut.FindStops("Central");

            // Assert
            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        [Fact]
        public async Task GetDepartures_ReturnsOk_WithTripSummaries()
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
                                Origin = new TripRequestResponseJourneyLegStop { DepartureTimeEstimated = "2025-09-01T06:41:00+10:00", DisassembledName = "Central" },
                                Destination = new TripRequestResponseJourneyLegStop { ArrivalTimeEstimated = "2025-09-01T07:10:00+10:00", DisassembledName = "Town Hall" }
                            }
                        }
                    }
                }
            };
            SetupTripClientResult(response);

            var sut = GetSystemUnderTest();

            // Act
            var result = await sut.GetDepartures("200080", "200060", "2025-09-01T06:00:00");

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var trips = Assert.IsAssignableFrom<List<TripSummary>>(okResult.Value);
            var trip = Assert.Single(trips);
            Assert.Equal("Central", trip.Origin.Place);
        }

        [Fact]
        public async Task GetDepartures_InvalidDateTime_Returns500()
        {
            // Arrange
            var sut = GetSystemUnderTest();

            // Act
            var result = await sut.GetDepartures("200080", "200060", "not-a-date");

            // Assert
            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        [Fact]
        public async Task GetDepartures_ClientThrows_Returns500()
        {
            // Arrange
            SetupTripClientThrows(new TripPlannerException("boom", 503, null, null, null));
            var sut = GetSystemUnderTest();

            // Act
            var result = await sut.GetDepartures("200080", "200060", "2025-09-01T06:00:00");

            // Assert
            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        [Fact]
        public async Task GetTrip_ReturnsOk_WithRawTripResponse()
        {
            // Arrange
            var response = new TripRequestResponse { Version = "10.5", Journeys = new List<TripRequestResponseJourney>() };
            SetupTripClientResult(response);

            var sut = GetSystemUnderTest();

            // Act
            var result = await sut.GetTrip("200080", "200060", "2025-09-01T06:00:00");

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Same(response, okResult.Value);
        }

        [Fact]
        public async Task GetTrip_InvalidDateTime_Returns500()
        {
            // Arrange
            var sut = GetSystemUnderTest();

            // Act
            var result = await sut.GetTrip("200080", "200060", "not-a-date");

            // Assert
            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }
    }
}
