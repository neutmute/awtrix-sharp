using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;

namespace AwtrixSharpWeb.Apps
{
    public class DirunalApp : AwtrixApp<AppConfig>
    {
        ITimerService _timerService;

        Dictionary<int, AwtrixSettings> _hourMap;

        public DirunalApp(
            ILogger logger
            , ITimerService timerService
            , AppConfig config
            , AwtrixAddress awtrixAddress
            , IAwtrixService awtrixService) 
            : base(logger, config, awtrixAddress, awtrixService)
        {
            _timerService = timerService;

        }

        protected override void Initialize()
        {
            _timerService.MinuteChanged += ClockTickMinute;
            Logger.LogInformation("DirunalDecorator initialized and listening for minute changes.");

            _hourMap = new()
            {
                { 6, new AwtrixSettings().SetBrightness(8) },
                { 7, new AwtrixSettings().SetGlobalTextColor("#FFFFFF") },
                { 19, new AwtrixSettings().SetGlobalTextColor("#FF0000").SetBrightness(1) }, 
            };

            if (Config.IsEnvironmentDev())
            {
                // Tick straight away for testing
                var fakeClockTick = new ClockTickEventArgs(DateTime.Now.AddMinutes(-DateTime.Now.Minute));
                ClockTickMinute(this, fakeClockTick);
            }
        }

        private void ClockTickMinute(object? sender, ClockTickEventArgs e)
        {
            var newTime = e.Time.ToLocalTime();

            if (newTime.Minute == 0)
            {
                if (_hourMap.ContainsKey(newTime.Hour))
                {
                    var message = _hourMap[newTime.Hour];
                    Logger.LogInformation($"Setting global settings");
                    _ = Set(message).Result;
                }
            }
        }

        
    }
}
