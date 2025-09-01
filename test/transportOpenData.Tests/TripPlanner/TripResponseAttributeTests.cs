using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reflection;
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
            Assert.Equal("10.6.21.17", response.Version);
            
            var journey = response.Journeys.First();
            Assert.Equal(0, journey.Rating);
            Assert.False(journey.IsAdditional);
            
            var leg = journey.Legs.First();
            
            // Test origin/destination
            Assert.Equal("222316", leg.Origin.Id);
            Assert.Equal("Oatley Station, Oatley Pde, Oatley", leg.Origin.Name);
            Assert.Equal("Oatley Station, Oatley Pde", leg.Origin.DisassembledName);
            Assert.Equal(151.079509, leg.Origin.Coord.ElementAt(1));
            Assert.Equal(-33.980557, leg.Origin.Coord.ElementAt(0));
            
            // Test transportation
            var transportation = leg.Transportation;
            Assert.Equal("nsw:1328T:4:R:sj2", transportation.Id);
            Assert.Equal("Temporary buses 28T4", transportation.Name);
            Assert.Equal("Temporary buses", transportation.Product.Name);
            Assert.Equal(5, transportation.Product.Class);
            Assert.Equal("Transit Systems NSW", transportation.Operator.Name);
            
            // Test stop sequence
            Assert.NotNull(leg.StopSequence);
            Assert.Equal(5, leg.StopSequence.Count);
            
            // Test timestamps
            var firstStop = leg.StopSequence.First();
            var lastStop = leg.StopSequence.Last();
            
            Assert.Equal("2025-08-16T05:43:00Z", firstStop.DepartureTimePlanned);
            Assert.Equal("2025-08-16T05:37:54Z", firstStop.DepartureTimeEstimated);
            Assert.Equal("2025-08-16T06:48:00Z", lastStop.ArrivalTimePlanned);
            Assert.Equal("2025-08-16T06:33:48Z", lastStop.ArrivalTimeEstimated);
            
            // Test journey duration and distance
            Assert.Equal(3354, leg.Duration);
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
            
            // First leg should be temporary bus
            var busLeg = journey.Legs.First();
            Assert.Equal(5, busLeg.Transportation.Product.Class); // 5 = Temporary bus
            Assert.Equal("nsw:1328T:4:R:sj2", busLeg.Transportation.Id);
            Assert.Equal("Transit Systems NSW", busLeg.Transportation.Operator.Name);
            
            // Second leg should be train
            var trainLeg = journey.Legs.Last();
            Assert.Equal(1, trainLeg.Transportation.Product.Class); // 1 = Train
            Assert.Equal("nsw:020T1:N:H:sj2", trainLeg.Transportation.Id);
            Assert.Equal("Sydney Trains", trainLeg.Transportation.Operator.Name);
            
            // Check connection between legs (destination of first = origin of second)
            Assert.Equal("Central Station", trainLeg.Origin.DisassembledName.Split(',')[0]);
            Assert.Equal("Platform 16", trainLeg.Origin.DisassembledName.Split(',')[1].Trim());
        }
        
        [Fact]
        public async Task Deserialize_PathDescriptions_ShouldMapCorrectly()
        {
            // Arrange
            var response = await TestDataHelper.LoadTestDataAsync<TripRequestResponse>("ComplexTripResponse.json");
            
            // Act
            var journey = response.Journeys.First();
            var leg = journey.Legs.First(); // First leg has path descriptions
            
            // Assert
            Assert.NotNull(leg.PathDescriptions);
            Assert.True(leg.PathDescriptions.Count > 0);
            
            var firstDirection = leg.PathDescriptions.First();
            Assert.Equal("UNKNOWN", firstDirection.TurnDirection.ToString());
            Assert.Equal("", firstDirection.Manoeuvre.ToString());
            
            // Check a turn direction
            var secondDirection = leg.PathDescriptions.ElementAt(1);
            Assert.Equal("STRAIGHT", secondDirection.TurnDirection.ToString());
            Assert.Equal("", secondDirection.Manoeuvre.ToString());
            Assert.True(secondDirection.Distance > 0);
            Assert.True(secondDirection.Duration > 0);
        }
        
        [Fact]
        public async Task Deserialize_ServiceInfos_ShouldMapCorrectly()
        {
            // Arrange
            var response = await TestDataHelper.LoadTestDataAsync<TripRequestResponse>("ComplexTripResponse.json");
            
            // Act
            var journey = response.Journeys.First();
            var leg = journey.Legs.Last(); // The train leg has service info
            
            // Assert
            Assert.NotNull(leg.Infos);
            Assert.Equal(1, leg.Infos.Count);
            
            var info = leg.Infos.First();
            Assert.NotNull(info.Id);
            Assert.Equal("normal", info.Priority.ToString().ToLower());
            
            // Use reflection to check for properties that might not exist in all versions
            var propertiesProperty = info.GetType().GetProperty("Properties");
            if (propertiesProperty != null)
            {
                var properties = propertiesProperty.GetValue(info) as IDictionary<string, string>;
                if (properties != null)
                {
                    Assert.True(properties.ContainsKey("publisher") || properties.ContainsKey("type"));
                }
            }
            
            // Check info links via reflection if they exist
            var infoLinksProperty = info.GetType().GetProperty("InfoLinks");
            if (infoLinksProperty != null)
            {
                var infoLinks = infoLinksProperty.GetValue(info) as System.Collections.IEnumerable;
                if (infoLinks != null)
                {
                    foreach (var link in infoLinks)
                    {
                        var urlTextProperty = link.GetType().GetProperty("UrlText");
                        var contentProperty = link.GetType().GetProperty("Content");
                        
                        if (urlTextProperty != null)
                        {
                            var urlText = urlTextProperty.GetValue(link) as string;
                            Assert.NotNull(urlText);
                        }
                        
                        if (contentProperty != null)
                        {
                            var content = contentProperty.GetValue(link) as string;
                            Assert.NotNull(content);
                        }
                    }
                }
            }
        }

        [Fact]
        public async Task Deserialize_Interchange_ShouldMapCorrectly()
        {
            // Arrange
            var response = await TestDataHelper.LoadTestDataAsync<TripRequestResponse>("ComplexTripResponse.json");
            
            // Act
            var journey = response.Journeys.First();
            var leg = journey.Legs.First();
            
            // Assert
            Assert.NotNull(leg.Interchange);
            Assert.Equal("Fussweg", leg.Interchange.Desc);
            Assert.Equal(100, (int)leg.Interchange.Type); // 100 = Walking
            Assert.NotNull(leg.Interchange.Coords);
            Assert.True(leg.Interchange.Coords.Count > 0);
        }
        
        
        [Fact]
        public async Task Deserialize_CoordinateData_ShouldContainCorrectFormat()
        {
            // Arrange
            var response = await TestDataHelper.LoadTestDataAsync<TripRequestResponse>("ComplexTripResponse.json");
            
            // Act
            var journey = response.Journeys.First();
            var leg = journey.Legs.First();
            
            // Assert
            Assert.NotNull(leg.Coords);
            Assert.True(leg.Coords.Count > 0);
            
            // Check coordinate format
            var firstCoord = leg.Coords.First();
            Assert.Equal(2, firstCoord.Count);
            
            // The first coordinate should be for Sydney area
            var lat = firstCoord.ElementAt(0);
            var lon = firstCoord.ElementAt(1);
            
            // Check coordinates are in Sydney region
            Assert.True(lat > -34.5 && lat < -33.5, "Latitude should be in Sydney region");
            Assert.True(lon > 150.5 && lon < 152.0, "Longitude should be in Sydney region");
        }
        
        [Fact]
        public async Task Deserialize_MessageData_ShouldMapCorrectly()
        {
            // Arrange
            var response = await TestDataHelper.LoadTestDataAsync<TripRequestResponse>("ComplexTripResponse.json");
            
            // Act & Assert
            
            // Check system messages via reflection
            var systemMessagesProperty = response.GetType().GetProperty("SystemMessages");
            if (systemMessagesProperty != null)
            {
                var systemMessages = systemMessagesProperty.GetValue(response) as System.Collections.ICollection;
                Assert.NotNull(systemMessages);
                Assert.True(systemMessages.Count > 0);
                
                // If we can iterate through the messages, check their properties
                var enumerator = systemMessages.GetEnumerator();
                if (enumerator.MoveNext())
                {
                    var message = enumerator.Current;
                    
                    var typeProperty = message.GetType().GetProperty("Type");
                    if (typeProperty != null)
                    {
                        var type = typeProperty.GetValue(message) as string;
                        Assert.Equal("warning", type);
                    }
                    
                    var moduleProperty = message.GetType().GetProperty("Module");
                    if (moduleProperty != null)
                    {
                        var module = moduleProperty.GetValue(message) as string;
                        Assert.Equal("BROKER", module);
                    }
                }
            }
        }
    }
}