using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using System.Xml.Linq;

namespace AwtrixSharpWeb.Apps
{
    public class TripTimerApp : ScheduledApp<TripTimerAppConfig>
    {
        private readonly ITripPlannerService _tripPlanner;
        private readonly ITimerService _timerService;

        internal List<DateTimeOffset> NextDepartures { get; set; }

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
            NextDepartures = new List<DateTimeOffset>();
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
            var alarmTimes = NextDepartures.Select(GetAlarmTime)
                .Where(alarmTime => alarmTime.prepareForDepartTime > Clock.Now)
                .OrderBy(alarmTime => alarmTime)
                .Select(at => at.prepareForDepartTime)
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
                var secondsToAlarm = (int) timeToAlarm.TotalSeconds;

                var thisSecond = e.Time.Second;
                var isOddSecond = thisSecond % 2 == 1;
                var spacer = isOddSecond ? " " : ":";

                // 12h saves a character, ToString("h") fails
                var hour = Clock.Now.Hour % 12;
                if (hour == 0) hour = 12;
                var hourString = hour.ToString();

                //var text = $"{hour}{spacer}{Clock.Now:mm} {secondsToAlarm}";
                //var text = $"{hour}{spacer}{Clock.Now:mm}->{nextAlarm.Minute}";
                var jsonFormat = @"[
	{
	  ""t"": ""(TIME_NOW)"",
	  ""c"": ""00FF00""
	},
	{
	  ""t"": "" ->(TIME_ALARM)"",
	  ""c"": ""FF0000""
	}
]";

                var text = jsonFormat
                    .Replace("(TIME_NOW)", $"{hourString}{spacer}{Clock.Now:mm}")
                    .Replace("(TIME_ALARM)", $"{nextAlarm.Minute}");

                var quantisedProgress = GetProgress(Clock, nextAlarm);
                var useProgress = quantisedProgress.quantized;
                if (isOddSecond)
                {
                    useProgress = quantisedProgress.quantizedBlink;
                }

                var message = new AwtrixAppMessage()
                    .SetText(text)
                    .SetStack(false)
                    .SetDuration(300)
                    .SetProgress(useProgress);

                if (secondsToAlarm < 20)
                {
                    message
                        .SetText("GO!")
                        .SetRainbow()
                        .SetProgress(100);
                }

                return message;
            }

        }

        internal static (int quantized, int quantizedBlink) GetProgress(IClock clock, DateTimeOffset nextAlarm)
        {
            const int ZeroFromMinutes = 5;

            var countFromSecs = (int) TimeSpan.FromMinutes(ZeroFromMinutes).TotalSeconds;
            var secondsSinceCountFrom = (int)(clock.Now - nextAlarm.AddMinutes(-ZeroFromMinutes)).TotalSeconds;
            var progress = secondsSinceCountFrom * 100 / countFromSecs;

            var quantizedProgress = AwtrixService.Quantize(progress);
            return quantizedProgress;
        }

        internal (DateTimeOffset originDepartTime, DateTimeOffset departForOriginTime, DateTimeOffset prepareForDepartTime) GetAlarmTime(DateTimeOffset originDepartTime)
        {
            var departForOriginTime = originDepartTime.Add(-Config.TimeToOrigin);
            var prepareForDepartTime = departForOriginTime.Add(-Config.TimeToPrepare);
            return (originDepartTime, departForOriginTime, prepareForDepartTime);
        }

        protected override async Task ActivateScheduledWork(CancellationTokenSource cts)
        {
            Logger.LogInformation($"Schedule has activated");

            var earliestDeparture = Clock.Now.Add(Config.TimeToOrigin).Add(Config.TimeToPrepare);

            var newDepartures = await _tripPlanner.GetNextDepartures(Config.StopIdOrigin, Config.StopIdDestination, earliestDeparture.LocalDateTime);
            NextDepartures.Clear();
            NextDepartures.AddRange(newDepartures);
            var departuresCsv = string.Join(", ", NextDepartures.Select(d => d.ToString("HH:mm:ss")));

            Logger.LogInformation($"{NextDepartures.Count} future depatures found: {departuresCsv}");

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


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
