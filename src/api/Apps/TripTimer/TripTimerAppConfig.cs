using AwtrixSharpWeb.Apps.Configs;

namespace AwtrixSharpWeb.Apps.TripTimer
{

    public class TripTimerAppConfig : ScheduledAppConfig
    {

        public string StopIdOrigin
        {
            get => GetConfig<string>("StopIdOrigin");
            set => SetConfig("StopIdOrigin", value);
        }

        public string StopIdDestination
        {
            get => GetConfig<string>("StopIdDestination");
            set => SetConfig("StopIdDestination", value);
        }


        /// <summary>
        /// Travel time to get to origin (optional, default 00:00:00)
        /// </summary>
        public TimeSpan TimeToOrigin
        {
            get => GetConfig<TimeSpan>("TimeToOrigin");
            set => SetConfig("TimeToOrigin", value);
        }


        /// <summary>
        /// How much time to get ready before leaving (optional, default 00:00:00)
        /// </summary>
        public TimeSpan TimeToPrepare
        {
            get => GetConfig<TimeSpan>("TimeToPrepare");
            set => SetConfig("TimeToPrepare", value);
        }

        public static readonly TimeSpan DefaultAlertDuration = TimeSpan.FromSeconds(40);

        /// <summary>The countdown's progress bar starts filling this long before the alarm; the alert must fit inside it.</summary>
        public static readonly TimeSpan MaxAlertDuration = TimeSpan.FromMinutes(5);

        /// <summary>
        /// How long before the alarm the alert (first ValueMap, or "GO!") replaces the countdown (optional, default 00:00:40)
        /// </summary>
        public TimeSpan AlertDuration
        {
            get => string.IsNullOrWhiteSpace(Config.Get("AlertDuration")) ? DefaultAlertDuration : GetConfig<TimeSpan>("AlertDuration");
            set => SetConfig("AlertDuration", value);
        }

        public override IReadOnlyList<string> Validate()
        {
            var errors = new List<string>(base.Validate());
            ValidateRequired(errors, "StopIdOrigin");
            ValidateRequired(errors, "StopIdDestination");
            ValidateTimeSpan(errors, "TimeToOrigin", required: false, mustBePositive: false);
            ValidateTimeSpan(errors, "TimeToPrepare", required: false, mustBePositive: false);
            var errorsBeforeAlertDuration = errors.Count;
            ValidateTimeSpan(errors, "AlertDuration", required: false, mustBePositive: true);
            if (errors.Count == errorsBeforeAlertDuration && AlertDuration >= MaxAlertDuration)
            {
                errors.Add($"AlertDuration: '{Config.Get("AlertDuration")}' must be less than {MaxAlertDuration}");
            }
            return errors;
        }
    }
}
