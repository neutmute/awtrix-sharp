using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TransportOpenData.TripPlanner
{
    /// <summary>
    /// Response from the Trip Planner API
    /// </summary>
    public class TripPlannerResponse
    {
        [JsonPropertyName("journeys")]
        public List<Journey>? Journeys { get; set; }
    }

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

    /// <summary>
    /// Transportation details for a leg
    /// </summary>
    public class Transportation
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("disassembledName")]
        public string? DisassembledName { get; set; }

        [JsonPropertyName("product")]
        public Product? Product { get; set; }
    }

    /// <summary>
    /// Product information for transportation
    /// </summary>
    public class Product
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("class")]
        public int Class { get; set; }

        [JsonPropertyName("iconId")]
        public int IconId { get; set; }
    }

    /// <summary>
    /// Fare information for a journey
    /// </summary>
    public class Fare
    {
        [JsonPropertyName("tickets")]
        public List<Ticket>? Tickets { get; set; }
    }

    /// <summary>
    /// Ticket information
    /// </summary>
    public class Ticket
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("price")]
        public double Price { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }
    }

    /// <summary>
    /// Response from the StopFinder API
    /// </summary>
    public class StopFinderResponse
    {
        [JsonPropertyName("locations")]
        public List<StopLocation>? Locations { get; set; }
    }

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

    /// <summary>
    /// Response from the Departure Monitor API
    /// </summary>
    public class DepartureMonitorResponse
    {
        [JsonPropertyName("stopEvents")]
        public List<StopEvent>? StopEvents { get; set; }
    }

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

    /// <summary>
    /// Platform information
    /// </summary>
    public class Platform
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }
}