using TransportOpenData.TripPlanner;

namespace AwtrixSharpWeb.Interfaces
{
    public interface ITripPlannerService
    {
        Task<StopFinderResponse> FindStops(string query);
        Task<List<DateTimeOffset>> GetNextDepartures(string originStopId, string destinationStopId, DateTime fromWhen);
        Task<TripRequestResponse> GetTrip(string originStopId, string destinationStopId, DateTime fromWhen);
    }
}