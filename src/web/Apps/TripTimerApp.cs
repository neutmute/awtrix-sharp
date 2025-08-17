using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace AwtrixSharpWeb.Apps
{


    public class TripTimerApp : ScheduledApp<TripTimerAppConfig>
    {
        private readonly ITripPlannerService _tripPlanner;
        private readonly ITimerService _timerService;

        List<DateTimeOffset> _nextDepartures;

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
            _nextDepartures = new List<DateTimeOffset>();
        }


        private void ClockTickMinute(object? sender, ClockTickEventArgs e)
        {
            Logger.LogDebug($"Clock ticked minute: {e.Time}");
        }

        private void ClockTickSecond(object? sender, ClockTickEventArgs e)
        {
            var message = BuildMessage(e);
            _ = AppUpdate(message).Result;
        }

        private AwtrixAppMessage BuildMessage(ClockTickEventArgs e)
        {
            var alarmTimes = _nextDepartures.Select(GetAlarmTime)
                .Where(alarmTime => alarmTime > Clock.Now)
                .OrderBy(alarmTime => alarmTime)
                .ToList();
            
            if (alarmTimes.Count == 0)
            {
                Logger.LogWarning("No future departures");
                _cts.Cancel();
                return new AwtrixAppMessage(); 
            }
            else
            {
                var nextAlarm = alarmTimes.First();
                var timeToAlarm = nextAlarm - Clock.Now;
                var secondsToAlarm = (int)timeToAlarm.TotalSeconds;

                var thisSecond = e.Time.Second;
                var isOddSecond = thisSecond % 2 == 1;
                var spacer = isOddSecond ? " " : ":";

                var text = $"{Clock.Now:MM}T{secondsToAlarm}";

                var progress = GetProgress(Clock, nextAlarm);
                if (isOddSecond)
                {
                    progress--;
                }

                var message = new AwtrixAppMessage()
                    .SetText(text)
                    .SetStack(false)
                    .SetDuration(300)
                    .SetProgress(progress);

                if (secondsToAlarm < 20)
                {
                    message.SetText("GO GO GO");
                    message.SetRainbow();
                }

                return message;
            }

        }

        static int Quantize(int value)
        {
            value = Math.Clamp(value, 0, 100);
            double step = 100.0 / 31.0; // 32 bins
            return (int)Math.Round(value / step, MidpointRounding.ToZero);
        }

        internal static int GetProgress(IClock clock, DateTimeOffset nextAlarm)
        {
            const int ZeroFromMinutes = 5;

            var countFromSecs = (int) TimeSpan.FromMinutes(ZeroFromMinutes).TotalSeconds;
            var secondsSinceCountFrom = (int)(clock.Now - nextAlarm.AddMinutes(-ZeroFromMinutes)).TotalSeconds;
            var progress = secondsSinceCountFrom * 100 / countFromSecs;

            //var quantizedProgress = Quantize(progress);
            return progress;
        }

        private DateTimeOffset GetAlarmTime(DateTimeOffset departure)
        {
            return departure.Add(-Config.TimeToOrigin).Add(-Config.TimeToPrepare);
        }

        protected override async Task ActivateScheduledWork(CancellationTokenSource cts)
        {
            Logger.LogInformation($"Schedule has activated");

            var earliestDeparture = Clock.Now.Add(Config.TimeToOrigin).Add(Config.TimeToPrepare);

            _nextDepartures = await _tripPlanner.GetNextDepartures(Config.StopIdOrigin, Config.StopIdDestination, earliestDeparture.LocalDateTime);
            var departuresCsv = string.Join(", ", _nextDepartures.Select(d => d.ToString("HH:mm:ss")));

            Logger.LogInformation($"{_nextDepartures.Count} future depatures found: {departuresCsv}");

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
