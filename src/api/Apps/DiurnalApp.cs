using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using System;
using System.Globalization;

namespace AwtrixSharpWeb.Apps
{
    public class DiurnalApp : AwtrixApp<AppConfig>
    {
        ITimerService _timerService;

        Dictionary<TimeSpan, List<Action<AwtrixSettings>>> _hourMap;

        public DiurnalApp(
            ILogger logger
            , ITimerService timerService
            , AppConfig config
            , AwtrixAddress awtrixAddress
            , IAwtrixService awtrixService) 
            : base(logger, config, awtrixAddress, awtrixService)
        {
            _timerService = timerService;
            _hourMap = new Dictionary<TimeSpan, List<Action<AwtrixSettings>>>();
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

            foreach(var time in Config.Config.Keys)
            {
                try
                {
                    var timeSpan = TimeSpan.ParseExact(time, "hhmm", CultureInfo.InvariantCulture);
                    var value = Config.Config[time];

                    var valueParts = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach(var keyPair in valueParts)
                    {
                        var keyPairParts = keyPair.Split('=', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (keyPairParts.Length == 2)
                        {
                            var settingKey = keyPairParts[0].ToLower();
                            var settingValue = keyPairParts[1];
                            if (!_hourMap.ContainsKey(timeSpan))
                            {
                                _hourMap[timeSpan] = new List<Action<AwtrixSettings>>();
                            }

                            var actions = _hourMap[timeSpan];

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

           // if (Config.Environment?.ToLowerInvariant() == "development" || string.IsNullOrEmpty(Config.Environment))
            {
                var currentTime = DateTime.Now.TimeOfDay;
                var nextSetting = _hourMap
                                    .Keys
                                    .Order()
                                    .Where(t => t < currentTime)
                                    .ToList();

                Logger.LogInformation("Replaying {Count} previous time entry settings", nextSetting.Count);

                foreach(var setting in nextSetting)
                {
                    // Tick straight away for testing
                    var triggerTime = DateTime.Now.Date.Add(setting);
                    var fakeClockTick = new ClockTickEventArgs(triggerTime);

                    ClockTickMinute(this, fakeClockTick);
                }
            }
        }

        private void ClockTickMinute(object? sender, ClockTickEventArgs e)
        {
            var currentTime = e.Time.ToLocalTime().TimeOfDay;

            if (_hourMap.ContainsKey(currentTime))
            {
                var actions = _hourMap[currentTime];
                var awtrixSetting = new AwtrixSettings();

                foreach(var action in actions)
                {
                    action(awtrixSetting);
                }   

                Logger.LogInformation($"{AwtrixAddress.BaseTopic} @ {currentTime}: Applying global setting {awtrixSetting}");
                _ = Set(awtrixSetting).Result;
            }
        }
        
    }
}
