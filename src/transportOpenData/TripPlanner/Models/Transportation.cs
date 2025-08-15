using System.Text.Json.Serialization;

namespace TransportOpenData.TripPlanner.Models
{
    /// <summary>
    /// Transportation details for a leg
    /// </summary>
    public class Transportation
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("disassembledName")]
        public string? DisassembledName { get; set; }

        [JsonPropertyName("product")]
        public Product? Product { get; set; }
    }
}