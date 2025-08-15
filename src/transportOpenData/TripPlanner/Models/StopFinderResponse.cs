using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TransportOpenData.TripPlanner.Models
{
    /// <summary>
    /// Response from the StopFinder API
    /// </summary>
    public class StopFinderResponse
    {
        [JsonPropertyName("locations")]
        public List<StopFinderLocation>? Locations { get; set; }
    }
}