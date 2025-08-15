using System.Text.Json.Serialization;

namespace TransportOpenData.TripPlanner.Models
{
    /// <summary>
    /// Represents a transportation mode available at a stop location
    /// </summary>
    public class Mode
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        
        [JsonPropertyName("type")]
        public string? Type { get; set; }
        
        [JsonPropertyName("number")]
        public string? Number { get; set; }
        
        [JsonPropertyName("product")]
        public Product? Product { get; set; }
    }
}