using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TransportOpenData.TripPlanner.Models
{
    /// <summary>
    /// Represents a location returned by the StopFinder API
    /// </summary>
    public class StopFinderLocation
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
        
        [JsonPropertyName("modes")]
        public List<Mode>? Modes { get; set; }
    }
}