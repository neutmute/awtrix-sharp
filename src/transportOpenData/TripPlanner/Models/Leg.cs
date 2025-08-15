using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TransportOpenData.TripPlanner.Models
{
    /// <summary>
    /// Represents a single leg of a journey
    /// </summary>
    public class Leg
    {
        [JsonPropertyName("origin")]
        public Location? Origin { get; set; }

        [JsonPropertyName("destination")]
        public Location? Destination { get; set; }

        [JsonPropertyName("transportation")]
        public Transportation? Transportation { get; set; }

        [JsonPropertyName("duration")]
        public int Duration { get; set; }
    }
}