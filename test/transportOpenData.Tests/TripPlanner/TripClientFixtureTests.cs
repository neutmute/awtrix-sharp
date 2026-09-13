using System.Net;
using TransportOpenData.Tests.Helpers;
using TransportOpenData.TripPlanner;
using Xunit;

namespace TransportOpenData.Tests.TripPlanner
{
    /// <summary>
    /// Fixtures pushed through the real generated TripClient and its own serializer settings (CR-40).
    /// TripRequestResponseDeserializationTests use hand-made case-insensitive options that production never uses.
    /// </summary>
    public class TripClientFixtureTests
    {
        private static string Fixture(string name) => File.ReadAllText(Path.Combine("TestData", name));

        private static async Task<TripRequestResponse> RequestAsync(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            var client = new TripClient(new HttpClient(StubHttpMessageHandler.Json(json, status)))
            {
                BaseUrl = "https://example.test/v1/tp"
            };

            return await client.Request2Async(
                outputFormat: OutputFormat5.RapidJSON,
                coordOutputFormat: CoordOutputFormat4.EPSG4326,
                depArrMacro: DepArrMacro.Dep,
                itdDate: "20250816",
                itdTime: "1530",
                type_origin: Type_origin.Any,
                name_origin: "222316",
                type_destination: Type_destination.Any,
                name_destination: "200070",
                calcNumberOfTrips: 5,
                wheelchair: null,
                excludedMeans: null,
                exclMOT_1: null,
                exclMOT_2: null,
                exclMOT_4: null,
                exclMOT_5: null,
                exclMOT_7: null,
                exclMOT_9: null,
                exclMOT_11: null,
                tfNSWTR: TfNSWTR.True,
                version: null,
                itOptionsActive: null,
                computeMonomodalTripBicycle: null,
                cycleSpeed: null,
                bikeProfSpeed: null,
                maxTimeBicycle: null,
                onlyITBicycle: null,
                useElevationData: null,
                elevFac: null,
                cancellationToken: CancellationToken.None);
        }

        [Fact]
        public async Task ComplexTripResponse_DeserializesThroughTheClient_WithRealtimeStatus()
        {
            var response = await RequestAsync(Fixture("ComplexTripResponse.json"));

            Assert.Equal(8, response.Journeys.Count);
            var firstLeg = response.Journeys.First().Legs.First();
            Assert.Equal(new[] { "MONITORED" }, firstLeg.RealtimeStatus);
            Assert.Equal(100, response.Journeys.ElementAt(1).Legs.First().Transportation.Product.Class); // leading walk (CR-27)
        }

        [Fact]
        public async Task NumericEnumValue_StillMaps()
        {
            var response = await RequestAsync(Fixture("ComplexTripResponse.json"));

            Assert.Equal(TripRequestResponseJourneyLegInterchangeType._100, response.Journeys.First().Legs.First().Interchange.Type);
        }

        [Fact]
        public async Task ErrorBodyWithStatus200_HasErrorAndNoJourneys()
        {
            var response = await RequestAsync(Fixture("ErrorResponse.json"));

            Assert.Equal("The application calling the API has not been authenticated.", response.Error.Message);
            Assert.Null(response.Journeys);
        }

        [Fact]
        public async Task KnownStopType_StillMapsFromItsWireName()
        {
            var response = await RequestAsync(Fixture("SuccessfulTripResponse.json"));

            Assert.Equal(TripRequestResponseJourneyLegStopType.Stop, response.Journeys.First().Legs.First().Origin.Type);
        }

        [Theory]
        [InlineData("\"gisPoint\"")]      // a value TfNSW uses elsewhere (DestinationType) but this enum lacks (CR-38)
        [InlineData("{\"x\": [1, 2]}")]   // an unexpected token shape
        [InlineData("12345")]             // an undefined number
        public async Task UnknownStopType_DoesNotFailTheResponse(string replacement)
        {
            var json = Fixture("SuccessfulTripResponse.json").Replace("\"type\": \"stop\"", "\"type\": " + replacement);

            var response = await RequestAsync(json);

            var leg = response.Journeys.First().Legs.First();
            Assert.Null(leg.Origin.Type);
            Assert.Equal("Central", leg.Origin.DisassembledName); // the rest of the object still deserializes
        }
    }
}
