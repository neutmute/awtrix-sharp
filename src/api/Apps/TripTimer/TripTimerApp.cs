using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Services.TripPlanner;

namespace AwtrixSharpWeb.Apps.TripTimer
{
    /// <summary>
    /// Calculate the best times to get ready and leave for the train station
    /// </summary>
    public class TripTimerApp : ScheduledApp<TripTimerAppConfig>
    {
        private readonly ITripPlannerService _tripPlanner;
        private readonly ITimerService _timerService;
        private volatile IReadOnlyList<TripSummary> _nextDepartures = Array.Empty<TripSummary>();

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

            VisualAlertBuffer = TimeSpan.FromSeconds(20);
        }

        /// <summary>
        /// The departures the countdown is built from, rounded to the minute. A snapshot; replaced atomically.
        /// </summary>
        internal IReadOnlyList<TripSummary> NextDepartures => _nextDepartures;

        /// <summary>
        /// Replace the departures the countdown uses. Safe to call from any thread while active
        /// (WS6's periodic refresh calls this with the activation's token).
        /// </summary>
        internal void SetDepartures(IEnumerable<TripSummary> departures)
        {
            // Round to the minute otherwise we get to alarm time and it isn't aligned to minute boundaries
            var rounded = departures.Select(d => d.AsRounded()).ToList();
            _nextDepartures = rounded;

            Logger.LogInformation("{Count} future departures computed (Prep -> Leave -> Departure):", rounded.Count);
            foreach (var departure in rounded)
            {
                Logger.LogInformation("{AlarmStages}", GetAlarmTime(departure));
            }
        }

        protected override async Task OnActivateAsync(ScheduledActivation activation)
        {
            Logger.LogInformation("Trip timer activated ({Trigger})", activation.Trigger);

            var message = new AwtrixAppMessage()
                .SetText("Starting trip timer")
                .SetStack(false);

            await Notify(message);

            // Find the earliest we could get to the train station and query from then
            var earliestDeparture = Clock.Now.Add(Config.TimeToOrigin).Add(Config.TimeToPrepare);

            var departures = await _tripPlanner
                .GetNextDepartures(Config.StopIdOrigin, Config.StopIdDestination, earliestDeparture.LocalDateTime)
                .WaitAsync(activation.Token);

            SetDepartures(departures);

            _timerService.SecondChanged += ClockTickSecond;
        }

        protected override Task OnDeactivateAsync(ScheduledActivation activation)
        {
            _timerService.SecondChanged -= ClockTickSecond;
            Logger.LogInformation("Trip timer activation #{Number} deactivated", activation.Number);
            return Task.CompletedTask;
        }

        private void ClockTickSecond(object? sender, ClockTickEventArgs e)
        {
            var activation = CurrentActivation;
            if (activation is not { IsEnded: false })
            {
                return; // a tick dispatched after the window ended, was superseded, or the app was disposed
            }

            // Runs inside FireAndLog so nothing can escape onto the timer loop
            _ = FireAndLog(async () =>
            {
                var message = BuildMessage(e.Time);
                if (message == null)
                {
                    // CR-19: nothing left to show. End this activation; deactivation (unsubscribe + AppClear)
                    // runs on the thread pool, never on the tick thread.
                    Logger.LogInformation("No future departures; ending trip timer activation #{Number}", activation.Number);
                    activation.Complete();
                    return;
                }

                if (!activation.IsEnded)
                {
                    await AppUpdate(message);
                }
            }, nameof(ClockTickSecond));
        }

        /// <summary>
        /// The countdown frame for <paramref name="tickTime"/>, or null when no alarm is in the future.
        /// </summary>
        internal AwtrixAppMessage? BuildMessage(DateTime tickTime)
        {
            var alarmTimes = NextDepartures.Select(GetAlarmTime)
                .Where(alarmTime => alarmTime.PrepareForDepartTime > Clock.Now)
                .Select(at => at.PrepareForDepartTime)
                .Order()
                .ToList();

            if (alarmTimes.Count == 0)
            {
                return null;
            }

            var nextAlarm = alarmTimes.First();
            var timeToAlarm = nextAlarm - Clock.Now;

            var clockText = TimerService.FormatClockString(tickTime, false);

            var nowColor = "00FF00";

            if (nextAlarm.AddMinutes(-1) <= Clock.Now)
            {
                // We are in the last minute before the alarm
                nowColor = "FFA500";
            }

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
                    message
                        .SetText("GO!")
                        .SetRainbow()
                        .SetProgress(100);

                    Logger.LogInformation("{Text}", message.Text);
                }
            }

            return message;
        }

        internal (int quantized, int quantizedBlink) GetProgress(IClock clock, DateTimeOffset nextAlarm)
        {
            const int ZeroFromMinutes = 5;

            var countFromSecs = (int)(TimeSpan.FromMinutes(ZeroFromMinutes) - VisualAlertBuffer).TotalSeconds; // ensure full progress bar
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
    }
}
