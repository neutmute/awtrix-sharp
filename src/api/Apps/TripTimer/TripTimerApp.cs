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

        /// <summary>How often an active trip timer re-queries departures (CR-25)</summary>
        internal static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(2);

        /// <summary>First retry delay after a failed refresh; doubles per consecutive failure, capped at <see cref="RefreshInterval"/></summary>
        internal static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

        private readonly object _refreshLock = new();
        private Task<bool>? _refreshInFlight;
        private ScheduledActivation? _refreshActivation;

        /// <summary>The activation the list and <see cref="_departuresLoaded"/> belong to. Written under <see cref="_refreshLock"/>.</summary>
        private ScheduledActivation? _departuresActivation;

        /// <summary>The activation that has already logged its end, so concurrent ticks end it once</summary>
        private ScheduledActivation? _endingActivation;

        /// <summary>True once a query in the current activation returned at least one departure</summary>
        private volatile bool _departuresLoaded;

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
            RefreshDelay = (delay, token) => Task.Delay(delay, Clock.TimeProvider, token);
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

        /// <summary>Waits between refreshes. Tests replace it to step the loop deterministically.</summary>
        internal Func<TimeSpan, CancellationToken, Task> RefreshDelay { get; set; }

        /// <summary>
        /// Re-query departures for <paramref name="activation"/>. Single-flight: concurrent callers share one request.
        /// Returns true when the list was replaced. Never throws; on failure the last good list is kept (CR-25).
        /// </summary>
        internal Task<bool> RefreshDeparturesAsync(ScheduledActivation activation)
        {
            lock (_refreshLock)
            {
                if (_refreshInFlight is { IsCompleted: false } && ReferenceEquals(_refreshActivation, activation))
                {
                    return _refreshInFlight;
                }

                _refreshActivation = activation;
                return _refreshInFlight = RefreshCoreAsync(activation);
            }
        }

        private async Task<bool> RefreshCoreAsync(ScheduledActivation activation)
        {
            try
            {
                // Find the earliest we could get to the train station and query from then
                var earliestDeparture = Clock.Now.Add(Config.TimeToOrigin).Add(Config.TimeToPrepare);

                var departures = await _tripPlanner.GetNextDepartures(
                    Config.StopIdOrigin, Config.StopIdDestination, earliestDeparture, activation.Token);

                if (activation.IsEnded)
                {
                    Logger.LogDebug("Discarding a late departures result for trip timer activation #{Number}", activation.Number);
                    return false; // a late answer for a window that has already ended
                }

                if (departures.Count == 0)
                {
                    // The service maps error bodies to an empty list, so empty never clears a good list
                    Logger.LogInformation("Trip planner returned no departures; keeping the {Count} already known", NextDepartures.Count);
                    return false;
                }

                lock (_refreshLock)
                {
                    // WS6 review m1: checked and written atomically with OnActivateAsync's reset, so a late answer for a
                    // superseded window can never land in (or mark as loaded) the next window
                    if (activation.IsEnded || !ReferenceEquals(_departuresActivation, activation))
                    {
                        Logger.LogDebug("Discarding a late departures result for trip timer activation #{Number}", activation.Number);
                        return false;
                    }

                    SetDepartures(departures);
                    _departuresLoaded = true;
                }

                return true;
            }
            catch (OperationCanceledException) when (activation.Token.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Refreshing departures failed; keeping the {Count} already known", NextDepartures.Count);
                return false;
            }
        }

        private async Task RefreshLoopAsync(ScheduledActivation activation)
        {
            var consecutiveFailures = _departuresLoaded ? 0 : 1;

            try
            {
                while (!activation.IsEnded)
                {
                    await RefreshDelay(NextRefreshDelay(consecutiveFailures), activation.Token);
                    consecutiveFailures = await RefreshDeparturesAsync(activation) ? 0 : consecutiveFailures + 1;
                }
            }
            catch (OperationCanceledException) when (activation.Token.IsCancellationRequested)
            {
                // The window ended; nothing to clean up
            }
        }

        internal static TimeSpan NextRefreshDelay(int consecutiveFailures)
        {
            if (consecutiveFailures <= 0)
            {
                return RefreshInterval;
            }

            var backoff = RetryInterval * (1 << Math.Min(consecutiveFailures - 1, 8));
            return backoff < RefreshInterval ? backoff : RefreshInterval;
        }

        /// <summary>
        /// The countdown has nothing to show. If nothing was loaded yet this activation, keep the window open and let the
        /// refresh loop retry. Otherwise re-query once, and end the window only if there is still nothing (CR-25, CR-19).
        /// </summary>
        private async Task EndIfNoDeparturesRemainAsync(ScheduledActivation activation, DateTime tickTime)
        {
            if (!_departuresLoaded)
            {
                return;
            }

            await RefreshDeparturesAsync(activation);

            if (activation.IsEnded || BuildMessage(tickTime) != null)
            {
                return;
            }

            // Ticks that queued up behind the re-query all get here; only the first ends the window (WS6 review m2)
            if (ReferenceEquals(Interlocked.Exchange(ref _endingActivation, activation), activation))
            {
                return;
            }

            // Deactivation (unsubscribe + AppClear) runs on the thread pool, never on the tick thread
            Logger.LogInformation("No future departures; ending trip timer activation #{Number}", activation.Number);
            activation.Complete();
        }

        protected override async Task OnActivateAsync(ScheduledActivation activation)
        {
            Logger.LogInformation("Trip timer activated ({Trigger})", activation.Trigger);

            // A new window starts from nothing: the previous window's list must not count as loaded
            lock (_refreshLock)
            {
                _departuresActivation = activation;
                _departuresLoaded = false;
                _nextDepartures = Array.Empty<TripSummary>();
            }

            // WS4 review: a window superseded while the base cleared the slot must not announce itself or call TfNSW
            activation.Token.ThrowIfCancellationRequested();

            var message = new AwtrixAppMessage()
                .SetText("Starting trip timer")
                .SetStack(false);

            await Notify(message);
            activation.Token.ThrowIfCancellationRequested();

            // Never throws; on failure the list stays empty and the refresh loop retries sooner (CR-25)
            await RefreshDeparturesAsync(activation);
            activation.Token.ThrowIfCancellationRequested();

            _timerService.SecondChanged += ClockTickSecond;

            _ = FireAndLog(() => RefreshLoopAsync(activation), "Refresh departures");
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
                    await EndIfNoDeparturesRemainAsync(activation, e.Time);
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

            if (clockText.Contains(":"))    // Even second: FormatClockString shows the colon
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
