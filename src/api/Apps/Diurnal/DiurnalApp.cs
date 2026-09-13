using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using System.Globalization;

namespace AwtrixSharpWeb.Apps.Diurnal
{
    /// <summary>
    /// Change brightness and colour based on time of day
    /// </summary>
    public class DiurnalApp : AwtrixApp<AppConfig>
    {
        private readonly ITimerService _timerService;
        private readonly IClock _clock;

        private readonly Dictionary<TimeSpan, List<Action<AwtrixSettings>>> _timeActionMap;

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
            _timeActionMap = new Dictionary<TimeSpan, List<Action<AwtrixSettings>>>();
        }

        protected override void Initialize()
        {
            _timerService.MinuteChanged += ClockTickMinute;

            // Check if Config dictionary is populated
            if (Config.Config == null || Config.Config.Count == 0)
            {
                Logger.LogWarning("DiurnalApp Config is empty. Make sure it's properly configured in appsettings.json");
                return;
            }

            Logger.LogDebug("DiurnalApp initializing with {Count} time entries", Config.Config.Count);

            foreach (var time in Config.Config.Keys)
            {
                try
                {
                    var timeSpan = TimeSpan.ParseExact(time, "hhmm", CultureInfo.InvariantCulture);
                    var value = Config.Config[time];

                    var valueParts = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var keyPair in valueParts)
                    {
                        var keyPairParts = keyPair.Split('=', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (keyPairParts.Length == 2)
                        {
                            var settingKey = keyPairParts[0].ToLower();
                            var settingValue = keyPairParts[1];
                            if (!_timeActionMap.ContainsKey(timeSpan))
                            {
                                _timeActionMap[timeSpan] = new List<Action<AwtrixSettings>>();
                            }

                            var actions = _timeActionMap[timeSpan];

                            switch (settingKey)
                            {
                                case "brightness":
                                    actions.Add(a => a.SetBrightness(byte.Parse(settingValue)));
                                    break;
                                case "globaltextcolor":
                                    actions.Add(a => a.SetGlobalTextColor(settingValue));
                                    break;
                                default:
                                    Logger.LogWarning("Unknown setting key '{SettingKey}' in config for hour {Hour}", settingKey, time);
                                    break;
                            }
                        }
                    }

                    Logger.LogDebug("Config Key: {Key} = {Value}", time, Config.Config[time]);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error processing time entry {Time}", time);
                }
            }

            // Replay today's earlier entries so the clock reflects the current period at startup
            var now = _clock.Now;
            var currentTime = now.TimeOfDay;
            var previousSettings = _timeActionMap
                                .Keys
                                .Order()
                                .Where(t => t < currentTime)
                                .ToList();

            Logger.LogInformation("Replaying {Count} previous time entry settings", previousSettings.Count);

            if (previousSettings.Count > 0)
            {
                // Merge in chronological order (later entries override earlier keys) and send a single Set,
                // so the device ends in the current period's state regardless of publish completion order.
                _ = FireAndLog(() => ReplaySettingsAsync(previousSettings), "ReplaySettings");
            }
        }

        /// <summary>
        /// WS4 (CR-31): detach from the timer on dispose. Keep this override when rewriting this file (WS5).
        /// </summary>
        protected override void ReleaseResources()
        {
            _timerService.MinuteChanged -= ClockTickMinute;
            base.ReleaseResources();
        }

        private async Task ReplaySettingsAsync(IReadOnlyList<TimeSpan> orderedEntries)
        {
            var merged = new AwtrixSettings();
            foreach (var entry in orderedEntries)
            {
                try
                {
                    foreach (var kv in BuildSettings(entry))
                    {
                        merged[kv.Key] = kv.Value;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "{BaseTopic} @ {Time}: failed to build settings during replay; skipping entry", AwtrixAddress.BaseTopic, entry);
                }
            }

            await PublishSettingsAsync(merged, orderedEntries[^1]);
        }

        private void ClockTickMinute(object? sender, ClockTickEventArgs e)
        {
            // e.Time is local wall-clock time (TimerService contract) - no ToLocalTime() conversion
            var time = e.Time.TimeOfDay;
            var minute = new TimeSpan(time.Hours, time.Minutes, 0);
            _ = FireAndLog(() => ApplySettingsAsync(minute), nameof(ClockTickMinute));
        }

        private async Task ApplySettingsAsync(TimeSpan minute)
        {
            if (!_timeActionMap.ContainsKey(minute))
            {
                return;
            }

            await PublishSettingsAsync(BuildSettings(minute), minute);
        }

        private AwtrixSettings BuildSettings(TimeSpan minute)
        {
            var awtrixSetting = new AwtrixSettings();
            if (_timeActionMap.TryGetValue(minute, out var actions))
            {
                foreach (var action in actions)
                {
                    action(awtrixSetting);
                }
            }
            return awtrixSetting;
        }

        private async Task PublishSettingsAsync(AwtrixSettings awtrixSetting, TimeSpan minute)
        {
            if (awtrixSetting.Count == 0)
            {
                Logger.LogWarning("{BaseTopic} @ {Time}: no valid settings to apply; skipping", AwtrixAddress.BaseTopic, minute);
                return;
            }

            Logger.LogInformation("{BaseTopic} @ {Time}: Applying global setting {AwtrixSetting}", AwtrixAddress.BaseTopic, minute, awtrixSetting);
            await Set(awtrixSetting);
        }
    }
}
