using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TransportOpenData.TripPlanner.Models
{
    /// <summary>
    /// Represents a journey from origin to destination
    /// </summary>
    public class Journey
    {
        [JsonPropertyName("legs")]
        public List<Leg>? Legs { get; set; }

        [JsonPropertyName("fare")]
        public Fare? Fare { get; set; }

        [JsonPropertyName("duration")]
        public int Duration { get; set; }
    }
}