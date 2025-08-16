using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using TransportOpenData.TripPlanner;
using Xunit;

namespace TransportOpenData.Tests.TripPlanner
{
    public class TripRequestResponseDeserializationTests
    {
        private readonly JsonSerializerOptions _jsonOptions;

        public TripRequestResponseDeserializationTests()
        {
            _jsonOptions = new JsonSerializerOptions();
            // Update any necessary JSON serializer settings to match what's in the API client
            UpdateJsonSerializerSettings(_jsonOptions);
        }

        // This method should match the same logic used in the TripPlannerClient.nswag.cs
        private static void UpdateJsonSerializerSettings(JsonSerializerOptions settings)
        {
            // Configure options to match what's in the API client
            settings.PropertyNameCaseInsensitive = true;
            settings.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        }

        [Fact]
        public async Task Deserialize_SuccessfulTripResponse_ShouldDeserializeCorrectly()
        {
            // Arrange
            string jsonContent = await File.ReadAllTextAsync("TestData/SuccessfulTripResponse.json");

            // Act
            var response = JsonSerializer.Deserialize<TripRequestResponse>(jsonContent, _jsonOptions);

            // Assert
            Assert.NotNull(response);
            Assert.Null(response.Error);
            Assert.NotNull(response.Journeys);
            Assert.NotEmpty(response.Journeys);
            
            // Add more detailed assertions about the expected response structure
            var firstJourney = response.Journeys.First();
            Assert.NotNull(firstJourney.Legs);
            Assert.NotEmpty(firstJourney.Legs);
        }

        [Fact]
        public async Task Deserialize_ErrorResponse_ShouldDeserializeCorrectly()
        {
            // Arrange
            string jsonContent = await File.ReadAllTextAsync("TestData/ErrorResponse.json");

            // Act
            var response = JsonSerializer.Deserialize<TripRequestResponse>(jsonContent, _jsonOptions);

            // Assert
            Assert.NotNull(response);
            Assert.NotNull(response.Error);
            Assert.NotNull(response.Error.Message);
            Assert.Null(response.Journeys); // Or could be empty list depending on the response
        }

        [Fact]
        public async Task Deserialize_EmptyJourneysResponse_ShouldDeserializeCorrectly()
        {
            // Arrange
            string jsonContent = await File.ReadAllTextAsync("TestData/EmptyJourneysResponse.json");

            // Act
            var response = JsonSerializer.Deserialize<TripRequestResponse>(jsonContent, _jsonOptions);

            // Assert
            Assert.NotNull(response);
            Assert.Null(response.Error);
            Assert.NotNull(response.Journeys);
            Assert.Empty(response.Journeys);
        }

        [Fact]
        public async Task Deserialize_ComplexResponse_ShouldHandleAllProperties()
        {
            // Arrange
            string jsonContent = await File.ReadAllTextAsync("TestData/ComplexTripResponse.json");

            // Act
            var response = JsonSerializer.Deserialize<TripRequestResponse>(jsonContent, _jsonOptions);

            // Assert
            Assert.NotNull(response);
            
            // Verify journey details
            Assert.NotNull(response.Journeys);
            
            foreach (var journey in response.Journeys)
            {
                Assert.NotNull(journey.Legs);
                
                foreach (var leg in journey.Legs)
                {
                    // Check essential leg properties
                    Assert.NotNull(leg.Origin);
                    Assert.NotNull(leg.Destination);
                    Assert.NotNull(leg.Transportation);
                    
                    // If coordinates are provided, ensure they're properly deserialized
                    if (leg.Coords != null)
                    {
                        foreach (var coord in leg.Coords)
                        {
                            Assert.Equal(2, coord.Count); // Each coordinate should have 2 values (lat, long)
                        }
                    }
                    
                    // Verify stop sequence if available
                    if (leg.StopSequence != null)
                    {
                        foreach (var stop in leg.StopSequence)
                        {
                            Assert.NotNull(stop.Name);
                            
                            // Test coordinate parsing
                            if (stop.Coord != null)
                            {
                                Assert.Equal(2, stop.Coord.Count);
                            }
                        }
                    }
                }
            }
        }
    }
}