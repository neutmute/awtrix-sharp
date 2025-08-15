using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TransportOpenData.TripPlanner.Models
{
    /// <summary>
    /// Represents a location (stop or station)
    /// </summary>
    public class Location
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("coord")]
        public List<double>? Coordinates { get; set; }

        [JsonPropertyName("departureTimeEstimated")]
        public DateTime? DepartureTimeEstimated { get; set; }

        [JsonPropertyName("arrivalTimeEstimated")]
        public DateTime? ArrivalTimeEstimated { get; set; }
    }
}