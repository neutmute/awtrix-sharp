using System;
using System.Text.Json.Serialization;

namespace TransportOpenData.TripPlanner.Models
{
    /// <summary>
    /// Represents a departure event at a stop
    /// </summary>
    public class StopEvent
    {
        [JsonPropertyName("location")]
        public Location? Location { get; set; }

        [JsonPropertyName("transportation")]
        public Transportation? Transportation { get; set; }

        [JsonPropertyName("departureTimePlanned")]
        public DateTime DepartureTimePlanned { get; set; }

        [JsonPropertyName("departureTimeEstimated")]
        public DateTime? DepartureTimeEstimated { get; set; }

        [JsonPropertyName("platform")]
        public Platform? Platform { get; set; }
    }
}