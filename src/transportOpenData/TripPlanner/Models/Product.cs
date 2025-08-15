using System.Text.Json.Serialization;

namespace TransportOpenData.TripPlanner.Models
{
    /// <summary>
    /// Product information for transportation
    /// </summary>
    public class Product
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("class")]
        public int Class { get; set; }

        [JsonPropertyName("iconId")]
        public int IconId { get; set; }
    }
}