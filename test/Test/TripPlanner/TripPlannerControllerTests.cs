using System.Net;
using AwtrixSharpWeb.Controllers;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TransportOpenData.TripPlanner;
using static Test.TripPlanner.TripPlannerTestData;

namespace Test.TripPlanner
{
    /// <summary>
    /// TripPlannerController over a real TripPlannerService whose generated clients talk to a stub handler.
    /// </summary>
    public class TripPlannerControllerTests
    {
        private static TripPlannerController Create(HttpMessageHandler handler) =>
            new(CreateService(handler), NullLogger<TripPlannerController>.Instance);

        private static StubHttpMessageHandler Throwing(Exception exception) =>
            new((_, _) => Task.FromException<HttpResponseMessage>(exception));

        [Fact]
        public async Task FindStops_ReturnsOk_WithServiceResult()
        {
            var sut = Create(StubHttpMessageHandler.Json("{\"version\":\"10.5\"}"));

            var result = await sut.FindStops("Central");

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("10.5", Assert.IsType<StopFinderResponse>(okResult.Value).Version);
        }

        [Fact]
        public async Task FindStops_ServiceThrows_Returns500()
        {
            var sut = Create(Throwing(new HttpRequestException("network error")));

            var result = await sut.FindStops("Central");

            Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
        }

        [Fact]
        public async Task GetDepartures_ReturnsOk_WithTripSummaries()
        {
            var sut = Create(StubHttpMessageHandler.Json(ToJson(Response(Journey(Leg(
                Stop("2025-09-01T06:41:00+10:00", null, "Central"),
                Stop("2025-09-01T07:10:00+10:00", null, "Town Hall")))))));

            var result = await sut.GetDepartures("200080", "200060", "2025-09-01T06:00:00");

            var okResult = Assert.IsType<OkObjectResult>(result);
            var trip = Assert.Single(Assert.IsAssignableFrom<List<TripSummary>>(okResult.Value));
            Assert.Equal("Central", trip.Origin.Place);
        }

        [Fact]
        public async Task GetDepartures_OffsetlessFromDateTime_IsSydneyWallClock()
        {
            // CR-26: used to be parsed as host-local time
            var handler = StubHttpMessageHandler.Json(ToJson(Response()));
            var sut = Create(handler);

            await sut.GetDepartures("200080", "200060", "2025-09-01T06:00:00");

            var uri = Assert.Single(handler.RequestUris).ToString();
            Assert.Contains("itdDate=20250901", uri);
            Assert.Contains("itdTime=0600", uri);
        }

        [Fact]
        public async Task GetDepartures_InvalidDateTime_Returns500()
        {
            var sut = Create(StubHttpMessageHandler.Json("{}"));

            var result = await sut.GetDepartures("200080", "200060", "not-a-date");

            Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
        }

        [Fact]
        public async Task GetDepartures_ApiReturns503_Returns500()
        {
            var sut = Create(StubHttpMessageHandler.Json("{}", HttpStatusCode.ServiceUnavailable));

            var result = await sut.GetDepartures("200080", "200060", "2025-09-01T06:00:00");

            Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
        }

        [Fact]
        public async Task GetTrip_ReturnsOk_WithRawTripResponse()
        {
            var sut = Create(StubHttpMessageHandler.Json("{\"version\":\"10.5\",\"journeys\":[]}"));

            var result = await sut.GetTrip("200080", "200060", "2025-09-01T06:00:00");

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("10.5", Assert.IsType<TripRequestResponse>(okResult.Value).Version);
        }

        [Fact]
        public async Task GetTrip_InvalidDateTime_Returns500()
        {
            var sut = Create(StubHttpMessageHandler.Json("{}"));

            var result = await sut.GetTrip("200080", "200060", "not-a-date");

            Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
        }
    }
}
