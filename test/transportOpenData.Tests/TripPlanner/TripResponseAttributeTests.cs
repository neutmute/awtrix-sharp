using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TransportOpenData.Tests.Helpers;
using TransportOpenData.TripPlanner;
using Xunit;

namespace TransportOpenData.Tests.TripPlanner
{
    public class TripResponseAttributeTests
    {
        private readonly JsonSerializerOptions _jsonOptions;

        public TripResponseAttributeTests()
        {
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        [Fact]
        public async Task Deserialize_JourneyAttributes_ShouldMapCorrectly()
        {
            // Arrange
            var response = await TestDataHelper.LoadTestDataAsync<TripRequestResponse>("ComplexTripResponse.json");
            
            // Act & Assert
            Assert.NotNull(response);
            Assert.Equal("10.5.17.3", response.Version);
            
            var journey = response.Journeys.First();
            Assert.Equal(5, journey.Rating);
            Assert.False(journey.IsAdditional);
            
            var leg = journey.Legs.First();
            
            // Test origin/destination
            Assert.Equal("10101100", leg.Origin.Id);
            Assert.Equal("Central Station", leg.Origin.Name);
            Assert.Equal("Central", leg.Origin.DisassembledName);
            Assert.Equal(151.20674, leg.Origin.Coord.ElementAt(0));
            Assert.Equal(-33.88289, leg.Origin.Coord.ElementAt(1));
            
            // Test transportation
            var transportation = leg.Transportation;
            Assert.Equal("T1", transportation.Id);
            Assert.Equal("T1 North Shore & Western Line", transportation.Name);
            Assert.Equal("Train", transportation.Product.Name);
            Assert.Equal(1, transportation.Product.Class);
            Assert.Equal("Sydney Trains", transportation.Operator.Name);
            
            // Test stop sequence
            Assert.NotNull(leg.StopSequence);
            Assert.Equal(4, leg.StopSequence.Count);
            
            // Test timestamps
            var firstStop = leg.StopSequence.First();
            var lastStop = leg.StopSequence.Last();
            
            Assert.Equal("2023-06-01T12:00:00Z", firstStop.DepartureTimePlanned);
            Assert.Equal("2023-06-01T12:01:00Z", firstStop.DepartureTimeEstimated);
            Assert.Equal("2023-06-01T12:10:00Z", lastStop.ArrivalTimePlanned);
            Assert.Equal("2023-06-01T12:11:00Z", lastStop.ArrivalTimeEstimated);
            
            // Test journey duration and distance
            Assert.Equal(600, leg.Duration);
            Assert.Equal(3000, leg.Distance);
            Assert.True(leg.IsRealtimeControlled);
        }
        
        [Fact]
        public async Task Deserialize_MultiLegJourney_ShouldContainAllLegs()
        {
            // Arrange
            var response = await TestDataHelper.LoadTestDataAsync<TripRequestResponse>("ComplexTripResponse.json");
            
            // Act
            var journey = response.Journeys.First();
            
            // Assert
            Assert.Equal(2, journey.Legs.Count);
            
            // First leg should be train
            var trainLeg = journey.Legs.First();
            Assert.Equal(1, trainLeg.Transportation.Product.Class); // 1 = Train
            Assert.Equal("T1", trainLeg.Transportation.Id);
            Assert.Equal("Sydney Trains", trainLeg.Transportation.Operator.Name);
            
            // Second leg should be ferry
            var ferryLeg = journey.Legs.Last();
            Assert.Equal(9, ferryLeg.Transportation.Product.Class); // 9 = Ferry
            Assert.Equal("F1", ferryLeg.Transportation.Id);
            Assert.Equal("Sydney Ferries", ferryLeg.Transportation.Operator.Name);
            
            // Check connection between legs (destination of first = origin of second)
            Assert.Equal(trainLeg.Destination.Id, ferryLeg.Origin.Id);
            Assert.Equal("Circular Quay", trainLeg.Destination.Name);
            Assert.Equal("Circular Quay", ferryLeg.Origin.Name);
        }
        
        [Fact]
        public async Task Deserialize_WalkingJourney_ShouldMapPathDescriptions()
        {
            // Arrange
            var response = await TestDataHelper.LoadTestDataAsync<TripRequestResponse>("ComplexTripResponse.json");
            
            // Act
            var walkingJourney = response.Journeys.Last(); // Second journey is the walking one
            var walkingLeg = walkingJourney.Legs.First();
            
            // Assert
            Assert.Equal(100, walkingLeg.Transportation.Product.Class); // 100 = Walking
            Assert.Equal("Walking", walkingLeg.Transportation.Name);
            
            // Check path descriptions
            Assert.NotNull(walkingLeg.PathDescriptions);
            Assert.Equal(2, walkingLeg.PathDescriptions.Count);
            
            var firstDirection = walkingLeg.PathDescriptions.First();
            Assert.Equal("Head north on Elizabeth St", firstDirection.Name);
            Assert.Equal("STRAIGHT", firstDirection.TurnDirection.ToString());
            Assert.Equal("LEAVE", firstDirection.Manoeuvre.ToString());
            Assert.Equal(500, firstDirection.Distance);
            Assert.Equal(360, firstDirection.Duration);
            
            var secondDirection = walkingLeg.PathDescriptions.Last();
            Assert.Equal("Turn right onto Park St", secondDirection.Name);
            Assert.Equal("RIGHT", secondDirection.TurnDirection.ToString());
            Assert.Equal(90, secondDirection.SkyDirection);
        }
        
        [Fact]
        public async Task Deserialize_ServiceAlerts_ShouldMapCorrectly()
        {
            // Arrange
            var response = await TestDataHelper.LoadTestDataAsync<TripRequestResponse>("ComplexTripResponse.json");
            
            // Act
            var journey = response.Journeys.First();
            var leg = journey.Legs.First();
            
            // Assert
            Assert.NotNull(leg.Infos);
            Assert.Equal(1, leg.Infos.Count);
            
            var alert = leg.Infos.First();
            Assert.Equal("alert_123", alert.Id);
            Assert.Equal("Trackwork between Central and Circular Quay may cause delays.", alert.Content);
            Assert.Equal("normal", alert.Priority.ToString().ToLower());
            Assert.Equal("Trackwork Alert", alert.Subtitle);
            Assert.Equal(1, alert.Version);
            Assert.Equal("http://example.com/alerts/123", alert.Url);
            Assert.Equal("More Information", alert.UrlText);
        }

        [Fact]
        public async Task Deserialize_Interchanges_ShouldMapCorrectly()
        {
            // Arrange
            var response = await TestDataHelper.LoadTestDataAsync<TripRequestResponse>("ComplexTripResponse.json");
            
            // Act
            var journey = response.Journeys.First();
            var leg = journey.Legs.First();
            
            // Assert
            Assert.NotNull(leg.Interchange);
            Assert.Equal("Walk from platform 2 to ferry wharf", leg.Interchange.Desc);
            Assert.Equal(99, (int)leg.Interchange.Type); // 99 = Walking
            Assert.NotNull(leg.Interchange.Coords);
            Assert.Equal(2, leg.Interchange.Coords.Count);
        }
        
        [Fact]
        public async Task Deserialize_FootPathInfo_ShouldMapCorrectly()
        {
            // Arrange
            var response = await TestDataHelper.LoadTestDataAsync<TripRequestResponse>("ComplexTripResponse.json");
            
            // Act
            var journey = response.Journeys.First();
            var leg = journey.Legs.First();
            
            // Assert
            Assert.NotNull(leg.FootPathInfo);
            Assert.Equal(1, leg.FootPathInfo.Count);
            
            var footPathInfo = leg.FootPathInfo.First();
            Assert.Equal(180, footPathInfo.Duration);
            Assert.Equal("AFTER", footPathInfo.Position.ToString());
            
            Assert.NotNull(footPathInfo.FootPathElem);
            Assert.Equal(1, footPathInfo.FootPathElem.Count);
            
            var element = footPathInfo.FootPathElem.First();
            Assert.Equal("Walk from platform to station exit", element.Description);
            Assert.Equal("LEVEL", element.Level.ToString());
            Assert.Equal("LEVEL", element.Type.ToString());
            Assert.Equal(0, element.LevelFrom);
            Assert.Equal(0, element.LevelTo);
            
            // Check origin and destination
            Assert.NotNull(element.Origin);
            Assert.NotNull(element.Destination);
            Assert.Equal("platform_exit", element.Origin.Georef);
            Assert.Equal(2, element.Origin.Platform);
            Assert.Equal("station_exit", element.Destination.Georef);
        }
    }
}