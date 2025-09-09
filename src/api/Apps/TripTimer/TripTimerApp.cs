using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Services.TripPlanner;
using System.Xml.Linq;

namespace AwtrixSharpWeb.Apps.TripTimer
{
    public class TripTimerApp : ScheduledApp<TripTimerAppConfig>
    {
        private readonly ITripPlannerService _tripPlanner;
        private readonly ITimerService _timerService;

        internal List<TripSummary> NextDepartures { get; set; }

        /// <summary>
        /// How long before the alarm actually triggers do we show the visual alert 
        /// </summary>
        private readonly TimeSpan VisualAlertBuffer;

        internal class AlarmStages
        {
            /// <summary>
            /// When you have to start getting ready to leave
            /// </summary>
            public DateTimeOffset PrepareForDepartTime { get; set; }


            /// <summary>
            /// When you have to leave for the origin station
            /// </summary>
            public DateTimeOffset DepartForOriginTime { get; set; }

            /// <summary>
            /// When the train departs from the origin station
            /// </summary>
            public DateTimeOffset OriginDepartTime { get; set; }

            public override string ToString()
            {
                return $"{PrepareForDepartTime:HH:mm} -> {DepartForOriginTime:HH:mm} -> {OriginDepartTime:HH:mm}";
            }
        }

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
            NextDepartures = new List<TripSummary>();

            VisualAlertBuffer = TimeSpan.FromSeconds(20);
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
                .Where(alarmTime => alarmTime.PrepareForDepartTime > Clock.Now)
                .Select(at => at.PrepareForDepartTime)
                .Order()                
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
                
                var clockText = TimerService.FormatClockString(e.Time, false);

                var nowColor = "00FF00";

                if (nextAlarm.AddMinutes(-1) <= Clock.Now)
                {
                    // We are in the last minute before the alarm
                    nowColor = "FFA500";
                }

                
                //var text = $"{hour}{spacer}{Clock.Now:mm} {secondsToAlarm}";
                //var text = $"{hour}{spacer}{Clock.Now:mm}->{nextAlarm.Minute}";
                var jsonFormat = @"[
	{
	  ""t"": ""(NOW_TIME)"",
	  ""c"": ""(NOW_COLOR)""
	},
	{
	  ""t"": "" ->(ALARM_TIME)"",
	  ""c"": ""FF0000""
	}
]";

                var text = jsonFormat
                    .Replace("(NOW_TIME)", clockText)
                    .Replace("(NOW_COLOR)", nowColor)
                    .Replace("(ALARM_TIME)", $"{nextAlarm:mm}");

                var quantisedProgress = GetProgress(Clock, nextAlarm);
                var useProgress = quantisedProgress.quantized;
                
                if (clockText.Contains(":"))    // Is an odd second
                {
                    useProgress = quantisedProgress.quantizedBlink;
                }

                var message = new AwtrixAppMessage()
                    .SetText(text)
                    .SetStack(false)
                    .SetDuration(300)
                    .SetProgress(useProgress);

                if (timeToAlarm < VisualAlertBuffer)
                {
                    if (Config.ValueMaps.Any())
                    {
                        Config
                            .ValueMaps[0]
                            .Decorate(message, Logger);
                    }
                    else
                    {
                        text = "GO!";
                        message
                            .SetText(text)
                            .SetRainbow()
                            .SetProgress(100);

                        Logger.LogInformation($"{message.Text}");
                    }
                }

                return message;
            }

        }

        internal (int quantized, int quantizedBlink) GetProgress(IClock clock, DateTimeOffset nextAlarm)
        {
            const int ZeroFromMinutes = 5;

            var countFromSecs = (int) (TimeSpan.FromMinutes(ZeroFromMinutes) - VisualAlertBuffer).TotalSeconds; // enure full progress bar
            var secondsSinceCountFrom = (int)(clock.Now - nextAlarm.AddMinutes(-ZeroFromMinutes)).TotalSeconds;
            var progress = secondsSinceCountFrom * 100 / countFromSecs;

            var quantizedProgress = AwtrixService.Quantize(progress);
            return quantizedProgress;
        }

        internal AlarmStages GetAlarmTime(TripSummary originDepartTime)
        {
            var departForOriginTime = originDepartTime.Origin.Time.Add(-Config.TimeToOrigin);
            var prepareForDepartTime = departForOriginTime.Add(-Config.TimeToPrepare);
            return new AlarmStages { OriginDepartTime = originDepartTime.Origin.Time, DepartForOriginTime = departForOriginTime, PrepareForDepartTime = prepareForDepartTime };
        }

        protected override async Task ActivateScheduledWork(CancellationTokenSource cts)
        {
            Logger.LogInformation($"Schedule has activated");

            // Find the earliest we could get to the train station and query from then
            var earliestDeparture = Clock.Now.Add(Config.TimeToOrigin).Add(Config.TimeToPrepare);

            var newDepartures = await _tripPlanner.GetNextDepartures(Config.StopIdOrigin, Config.StopIdDestination, earliestDeparture.LocalDateTime);
            NextDepartures.Clear();

            foreach(var departure in newDepartures)
            {
                Logger.LogInformation("Raw Departure: {departure}", departure);
            }

            // Round to the minute otherwise we get to alarm time and it isn't aligned to minute boundaries
            NextDepartures.AddRange(newDepartures.Select(d => d.AsRounded()));
            
            var departuresCsv = string.Join(Environment.NewLine, NextDepartures.Select(d => GetAlarmTime(d).ToString()));

            Logger.LogInformation($"{NextDepartures.Count} future departures computed:");
            Logger.LogInformation($"Prep -> Leave -> Departure");
            NextDepartures.ForEach(d => Logger.LogInformation(GetAlarmTime(d).ToString()));

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
                Deactivate();
            }
        }

        private void Deactivate()
        {
            Logger.LogInformation($"Schedule deactivating");
            _timerService.SecondChanged -= ClockTickSecond;
            _timerService.MinuteChanged -= ClockTickMinute;
        }


        new protected void Dispose(bool disposing)
        {
            if (disposing)
            {
                Deactivate();
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
