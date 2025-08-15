using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TransportOpenData.TripPlanner.Models
{
    /// <summary>
    /// Response from the Departure Monitor API
    /// </summary>
    public class DepartureMonitorResponse
    {
        [JsonPropertyName("stopEvents")]
        public List<StopEvent>? StopEvents { get; set; }
    }
}