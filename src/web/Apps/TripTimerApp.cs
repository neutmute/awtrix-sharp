using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;

namespace AwtrixSharpWeb.Apps
{

    public class TripTimerApp : AwtrixApp
    {
        private readonly TripPlannerService _tripPlanner;
        private readonly TripTimerAppConfig _config;

        public TripTimerApp(
            AwtrixAddress awtrixAddress
            , AwtrixService awtrixService
            , TripTimerAppConfig config
            , TripPlannerService tripPlanner) : base(awtrixAddress, awtrixService)
        {
            _tripPlanner = tripPlanner;
            _config = config;
        }

        public override void Initialize()
        {
        }

    }
}
