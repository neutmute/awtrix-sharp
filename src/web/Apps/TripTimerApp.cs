using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging;

namespace AwtrixSharpWeb.Apps
{


    public class TripTimerApp : ScheduledApp<TripTimerAppConfig>
    {
        private readonly ITripPlannerService _tripPlanner;
        private readonly ITimerService _timerService;

        public TripTimerApp(
            ILogger logger
            , IClock clock
            , AwtrixAddress awtrixAddress
            , IAwtrixService awtrixService
            , ITimerService timerService
            , TripTimerAppConfig config
            , ITripPlannerService tripPlanner) : base(logger, clock, awtrixAddress, awtrixService, config)
        {
            _tripPlanner = tripPlanner;
            _timerService = timerService;
        }


        private void ClockTickMinute(object? sender, ClockTickEventArgs e)
        {
            Logger.LogInformation($"Clock ticked minute: {e.Time}");    
        }

        private void ClockTickSecond(object? sender, ClockTickEventArgs e)
        {
            Logger.LogInformation($"Clock ticked second: {e.Time}");
        }

        protected override async Task ActivateScheduledWork(CancellationTokenSource cts)
        {
            Logger.LogInformation($"Schedule has activated");
            _timerService.SecondChanged += ClockTickSecond;
            _timerService.MinuteChanged += ClockTickMinute;

            try
            {
                await WaitForCancellation(cts.Token);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in ActivateScheduledWork: {ex.Message}");
            }
            finally
            {
                Logger.LogInformation($"Schedule deactivating");
                _timerService.SecondChanged -= ClockTickSecond;
                _timerService.MinuteChanged -= ClockTickMinute;
            }
        }


        // Clean up resources
        new protected void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Unsubscribe from events
                _timerService.SecondChanged -= ClockTickSecond;
                _timerService.MinuteChanged -= ClockTickMinute;
            }
            
            base.Dispose(disposing);
        }

        // Override the non-protected Dispose method to call our new protected Dispose method
        public new void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
