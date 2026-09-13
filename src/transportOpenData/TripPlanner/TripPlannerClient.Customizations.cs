using System.Text.Json;
using System.Text.Json.Serialization;

namespace TransportOpenData.TripPlanner
{
    // Hand-written partials for the NSwag-generated TripPlannerClient.nswag.cs. Regenerating that file keeps these.

    public partial class TripClient
    {
        static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => LenientEnumJson.Apply(settings);
    }

    public partial class StopfinderClient
    {
        static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => LenientEnumJson.Apply(settings);
    }

    public partial class AddinfoClient
    {
        static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => LenientEnumJson.Apply(settings);
    }

    public partial class CoordClient
    {
        static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => LenientEnumJson.Apply(settings);
    }

    public partial class DmClient
    {
        static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => LenientEnumJson.Apply(settings);
    }

    public partial class TripRequestResponseJourneyLeg
    {
        /// <summary>
        /// Realtime flags for this leg, e.g. ["MONITORED"] in the fixtures. Present in responses but not in the published
        /// schema, so NSwag dropped it (CR-28). The value TfNSW uses for cancellations is not confirmed from repo data;
        /// consumers treat any value containing "CANCEL" as cancelled.
        /// </summary>
        [JsonPropertyName("realtimeStatus")]
        public ICollection<string>? RealtimeStatus { get; set; }
    }
}
