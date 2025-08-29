namespace AwtrixSharpWeb.Apps.Configs
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
        /// Travel time to get to origin
        /// </summary>
        public TimeSpan TimeToOrigin

        {
            get => GetConfig<TimeSpan>("TimeToOrigin");
            set => SetConfig("TimeToOrigin", value);
        }


        /// <summary>
        /// How much time to get ready before leaving
        /// </summary>
        public TimeSpan TimeToPrepare
        {
            get => GetConfig<TimeSpan>("TimeToPrepare");
            set => SetConfig("TimeToPrepare", value);
        }

    }
}
