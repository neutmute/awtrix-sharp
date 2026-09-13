using System.Net;
using System.Text;
using System.Text.Json;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TransportOpenData;
using TransportOpenData.TripPlanner;

namespace Test.TripPlanner
{
    /// <summary>
    /// Answers every request from a delegate, so the real generated clients and their serializer settings run without a network.
    /// </summary>
    internal sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        public List<Uri> RequestUris { get; } = new();

        public static StubHttpMessageHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new((_, _) => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (RequestUris)
            {
                RequestUris.Add(request.RequestUri!);
            }

            return _respond(request, cancellationToken);
        }
    }

    internal sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public List<string> CreatedNames { get; } = new();

        public HttpClient CreateClient(string name)
        {
            lock (CreatedNames)
            {
                CreatedNames.Add(name);
            }

            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    internal static class TripPlannerTestData
    {
        public const string BaseUrl = "https://example.test/v1/tp";

        /// <summary>TfNSW fixtures linked from test/transportOpenData.Tests/TestData</summary>
        public static string Fixture(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TripPlanner", "TestData", fileName));

        /// <summary>Serialises with default options; the generated [JsonPropertyName] attributes give the wire names</summary>
        public static string ToJson(TripRequestResponse response) => JsonSerializer.Serialize(response);

        public static TripPlannerService CreateService(HttpMessageHandler handler, ILogger<TripPlannerService>? logger = null, string baseUrl = BaseUrl) =>
            new(new StubHttpClientFactory(handler),
                Options.Create(new TransportOpenDataConfig { ApiKey = "test-key", BaseUrl = baseUrl }),
                logger ?? NullLogger<TripPlannerService>.Instance);

        public static TripRequestResponseJourneyLegStop Stop(string? estimated, string? planned, string name) => new()
        {
            ArrivalTimeEstimated = estimated,
            ArrivalTimePlanned = planned,
            DepartureTimeEstimated = estimated,
            DepartureTimePlanned = planned,
            DisassembledName = name
        };

        public static TripRequestResponseJourneyLeg Leg(
            TripRequestResponseJourneyLegStop origin,
            TripRequestResponseJourneyLegStop destination,
            int? productClass = null,
            params string[] realtimeStatus) => new()
        {
            Origin = origin,
            Destination = destination,
            Transportation = productClass is null ? null : new TripTransportation { Product = new RouteProduct { Class = productClass } },
            RealtimeStatus = realtimeStatus.Length == 0 ? null : realtimeStatus
        };

        public static TripRequestResponseJourney Journey(params TripRequestResponseJourneyLeg[] legs) => new() { Legs = legs.ToList() };

        public static TripRequestResponse Response(params TripRequestResponseJourney?[] journeys) => new() { Journeys = journeys.ToList()! };
    }
}
