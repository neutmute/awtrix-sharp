using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;

namespace AwtrixSharpWeb.Apps.Diurnal
{
    /// <summary>
    /// Change brightness and colour based on time of day.
    /// At startup the settings in effect now (including yesterday's carry-over) are restored in one publish;
    /// afterwards every entry in (last processed minute, current minute] is applied, so stalls, coalesced
    /// ticks, suspend and DST gaps never skip a setting.
    /// </summary>
    public class DiurnalApp : AwtrixApp<AppConfig>
    {
        private readonly ITimerService _timerService;
        private readonly IClock _clock;
        private readonly object _gate = new();

        private DiurnalSchedule _schedule = DiurnalSchedule.Empty;
        private DateTime _lastProcessed;

        public DiurnalApp(
            ILogger logger
            , IClock clock
            , ITimerService timerService
            , AppConfig config
            , AwtrixAddress awtrixAddress
            , IAwtrixService awtrixService)
            : base(logger, config, awtrixAddress, awtrixService)
        {
            _clock = clock;
            _timerService = timerService;
        }

        protected override void Initialize()
        {
            _schedule = DiurnalSchedule.Parse(Config.Config, Logger);

            if (_schedule.IsEmpty)
            {
                Logger.LogWarning("DiurnalApp on {BaseTopic} has no valid time entries; nothing will be scheduled. Check its Config in appsettings.json", AwtrixAddress.BaseTopic);
                return;
            }

            Logger.LogDebug("DiurnalApp on {BaseTopic} initialised with {Count} time entries", AwtrixAddress.BaseTopic, _schedule.Entries.Count);

            // IClock is backed by TimeProvider.GetLocalNow(): DateTime is the local wall-clock time
            var now = _clock.Now.DateTime;
            lock (_gate)
            {
                _lastProcessed = DiurnalSchedule.TruncateToMinute(now);
            }

            _timerService.MinuteChanged += ClockTickMinute;

            var state = _schedule.StateAt(now);
            Logger.LogInformation("{BaseTopic}: restoring Diurnal settings in effect at {Time:HH:mm}: {AwtrixSetting}", AwtrixAddress.BaseTopic, now, state);
            _ = FireAndLog(() => Set(state), "DiurnalStartupRestore");
        }

        /// <summary>
        /// WS4 (CR-31): detach from the timer on dispose. Keep this override when rewriting this file.
        /// Removing a handler that was never added (empty schedule) is a no-op.
        /// </summary>
        protected override void ReleaseResources()
        {
            _timerService.MinuteChanged -= ClockTickMinute;
            base.ReleaseResources();
        }

        private void ClockTickMinute(object? sender, ClockTickEventArgs e)
        {
            // e.Time is local wall-clock time (TimerService contract)
            _ = FireAndLog(() => ApplyDueAsync(e.Time), nameof(ClockTickMinute));
        }

        private async Task ApplyDueAsync(DateTime tickTime)
        {
            var now = DiurnalSchedule.TruncateToMinute(tickTime);
            DateTime from;

            lock (_gate)
            {
                from = _lastProcessed;
                _lastProcessed = now;
            }

            if (now < from)
            {
                Logger.LogInformation("{BaseTopic}: clock moved back from {From:HH:mm} to {Now:HH:mm}; Diurnal resumes from the new time", AwtrixAddress.BaseTopic, from, now);
                return;
            }

            var due = _schedule.DueBetween(from, now);
            if (due.Count == 0)
            {
                return;
            }

            Logger.LogInformation("{BaseTopic} @ {Time:HH:mm}: Applying global setting {AwtrixSetting}", AwtrixAddress.BaseTopic, now, due);
            await Set(due);
        }
    }
}
