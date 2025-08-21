using AwtrixSharpWeb.Domain;
using TransportOpenData.TripPlanner;

namespace AwtrixSharpWeb.Interfaces
{
    public interface ITripPlannerService
    {
        Task<StopFinderResponse> FindStops(string query);
        Task<List<TripSummary>> GetNextDepartures(string originStopId, string destinationStopId, DateTime fromWhen);
        Task<TripRequestResponse> GetTrips(string originStopId, string destinationStopId, DateTime fromWhen);
    }
}