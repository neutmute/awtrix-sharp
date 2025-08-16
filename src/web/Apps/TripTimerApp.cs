using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;

namespace AwtrixSharpWeb.Apps
{

    public class TripTimerApp : AwtrixApp
    {
        private readonly TripPlannerService _tripPlanner;
        private readonly TripTimerAppConfig _config;
        private readonly TimerService _timerService;

        public TripTimerApp(
            AwtrixAddress awtrixAddress
            , AwtrixService awtrixService
            , TimerService timerService
            , TripTimerAppConfig config
            , TripPlannerService tripPlanner) : base(awtrixAddress, awtrixService)
        {
            _tripPlanner = tripPlanner;
            _config = config;
            _timerService = timerService;
        }

        public override void Initialize()
        {
        }

    }
}
