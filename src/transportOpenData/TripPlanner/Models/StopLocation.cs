using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TransportOpenData.TripPlanner.Models
{
    /// <summary>
    /// Represents a stop location
    /// </summary>
    public class StopLocation
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("disassembledName")]
        public string? DisassembledName { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("coord")]
        public List<double>? Coordinates { get; set; }
    }
}