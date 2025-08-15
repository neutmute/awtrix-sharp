using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TransportOpenData.TripPlanner.Models
{
    /// <summary>
    /// Fare information for a journey
    /// </summary>
    public class Fare
    {
        [JsonPropertyName("tickets")]
        public List<Ticket>? Tickets { get; set; }
    }
}