using AwtrixSharpWeb.Services.TripPlanner;
using TransportOpenData.TripPlanner;

namespace AwtrixSharpWeb.Interfaces
{
    public interface ITripPlannerService
    {
        Task<StopFinderResponse> FindStops(string query, CancellationToken cancellationToken = default);

        /// <summary>
        /// Departures after <paramref name="fromWhen"/> (an instant; converted to Sydney time for the API).
        /// HTTP failures throw; malformed journeys are skipped.
        /// </summary>
        Task<List<TripSummary>> GetNextDepartures(string originStopId, string destinationStopId, DateTimeOffset fromWhen, CancellationToken cancellationToken = default);

        Task<TripRequestResponse> GetTrips(string originStopId, string destinationStopId, DateTimeOffset fromWhen, CancellationToken cancellationToken = default);
    }
}
