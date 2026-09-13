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

        public override IReadOnlyList<string> Validate()
        {
            var errors = new List<string>(base.Validate());
            ValidateRequired(errors, "StopIdOrigin");
            ValidateRequired(errors, "StopIdDestination");
            ValidateTimeSpan(errors, "TimeToOrigin", required: false, mustBePositive: false);
            ValidateTimeSpan(errors, "TimeToPrepare", required: false, mustBePositive: false);
            return errors;
        }
    }
}
