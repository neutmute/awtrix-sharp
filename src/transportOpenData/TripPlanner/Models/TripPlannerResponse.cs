using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TransportOpenData.TripPlanner.Models
{
    /// <summary>
    /// Response from the Trip Planner API
    /// </summary>
    public class TripPlannerResponse
    {
        [JsonPropertyName("journeys")]
        public List<Journey>? Journeys { get; set; }
    }
}