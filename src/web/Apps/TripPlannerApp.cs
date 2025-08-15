using System;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging;
using TransportOpenData.TripPlanner;

namespace AwtrixSharpWeb.Apps
{
    public class TripPlannerApp : IDisposable
    {
        private readonly AwtrixAddress _awtrixAddress;
        private readonly TripPlannerClient _tripPlannerClient;
        private readonly AwtrixService _awtrixService;
        private readonly ILogger<TripPlannerApp> _logger;
        private readonly System.Timers.Timer _updateTimer;
        
        // Default stops to monitor - could be configurable in the future
        private string _originStopId;
        private string _destinationStopId;
        private string _originName;
        private string _destinationName;

        public TripPlannerApp(
            AwtrixAddress awtrixAddress,
            TripPlannerClient tripPlannerClient,
            AwtrixService awtrixService,
            ILogger<TripPlannerApp> logger)
        {
            _awtrixAddress = awtrixAddress;
            _tripPlannerClient = tripPlannerClient;
            _awtrixService = awtrixService;
            _logger = logger;
            
            // Default to empty, will be set in Initialize
            _originStopId = string.Empty;
            _destinationStopId = string.Empty;
            _originName = string.Empty;
            _destinationName = string.Empty;

            // Create a timer to update the display every 5 minutes
            _updateTimer = new System.Timers.Timer(5 * 60 * 1000); // 5 minutes
            _updateTimer.Elapsed += OnTimerElapsed;
        }

        public async Task Initialize(string originStopName, string destinationStopName)
        {
            _logger.LogInformation("Initializing Trip Planner App with origin: {Origin}, destination: {Destination}", 
                originStopName, destinationStopName);

            try
            {
                // Find the origin stop
                var originStops = await _tripPlannerClient.FindStopsAsync(originStopName);
                if (originStops?.Locations?.Any() == true)
                {
                    var originStop = originStops.Locations.First();
                    _originStopId = originStop.Id ?? string.Empty;
                    _originName = originStop.Name ?? originStopName;
                    _logger.LogInformation("Found origin stop: {Id} - {Name}", _originStopId, _originName);
                }
                else
                {
                    _logger.LogWarning("Could not find origin stop: {Name}", originStopName);
                }

                // Find the destination stop
                var destinationStops = await _tripPlannerClient.FindStopsAsync(destinationStopName);
                if (destinationStops?.Locations?.Any() == true)
                {
                    var destinationStop = destinationStops.Locations.First();
                    _destinationStopId = destinationStop.Id ?? string.Empty;
                    _destinationName = destinationStop.Name ?? destinationStopName;
                    _logger.LogInformation("Found destination stop: {Id} - {Name}", _destinationStopId, _destinationName);
                }
                else
                {
                    _logger.LogWarning("Could not find destination stop: {Name}", destinationStopName);
                }

                if (!string.IsNullOrEmpty(_originStopId) && !string.IsNullOrEmpty(_destinationStopId))
                {
                    // Start the timer to update periodically
                    _updateTimer.Start();
                    
                    // Do an immediate update
                    await UpdateDisplay();
                }
                else
                {
                    _logger.LogError("Trip Planner App could not be initialized - stop IDs not found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing Trip Planner App");
            }
        }

        private async void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            await UpdateDisplay();
        }

        private async Task UpdateDisplay()
        {
            try
            {
                _logger.LogInformation("Updating Trip Planner display");

                // Get trip information
                var tripInfo = await _tripPlannerClient.GetTripAsync(_originStopId, _destinationStopId);
                
                if (tripInfo?.Journeys?.Any() == true)
                {
                    // Get the first (best) journey
                    var journey = tripInfo.Journeys.First();
                    
                    // Calculate information to display
                    int totalMinutes = journey.Duration / 60; // Convert seconds to minutes
                    
                    // Get the first leg departure time
                    var firstLeg = journey.Legs?.FirstOrDefault();
                    var departureTime = firstLeg?.Origin?.DepartureTimeEstimated;
                    
                    string timeDisplay;
                    if (departureTime.HasValue)
                    {
                        var localTime = departureTime.Value.ToLocalTime();
                        timeDisplay = localTime.ToString("h:mm tt");
                    }
                    else
                    {
                        timeDisplay = "Unknown";
                    }

                    // Create a message to display on the Awtrix device
                    var message = new AwtrixAppMessage()
                        .SetText($"{_originName} ? {_destinationName}: {totalMinutes}min @ {timeDisplay}")
                        .SetScrollSpeed(30)
                        .SetColor(new int[] { 0, 255, 0 }); // Green text

                    // Update the app on the Awtrix device
                    await _awtrixService.AppUpdate(_awtrixAddress, "tripplanner", message);
                }
                else
                {
                    _logger.LogWarning("No journey information found");
                    
                    // Display error message
                    var message = new AwtrixAppMessage()
                        .SetText($"No trips found from {_originName} to {_destinationName}")
                        .SetScrollSpeed(30)
                        .SetColor(new int[] { 255, 0, 0 }); // Red text for error

                    await _awtrixService.AppUpdate(_awtrixAddress, "tripplanner", message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating Trip Planner display");
            }
        }

        public void Dispose()
        {
            _updateTimer.Stop();
            _updateTimer.Elapsed -= OnTimerElapsed;
            _updateTimer.Dispose();
        }
    }
}