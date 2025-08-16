namespace AwtrixSharpWeb.Domain
{
    public class AppConfig
    {
        public string Name { get; set; } = string.Empty;
    }

    public class ScheduledAppConfig : AppConfig
    {
        public string CronSchedule { get; set; }

        /// <summary>
        /// How long to take over the clock for
        /// </summary>
        public TimeSpan ActiveTime { get; set; }
    }

    public class TripTimerAppConfig : ScheduledAppConfig 
    {

        public string StopIdOrigin { get; set; } = string.Empty;

        public string StopIdDestination { get; set; } = string.Empty;


        /// <summary>
        /// Travel time to get to origin
        /// </summary>
        public TimeSpan TimeToOrigin { get; set; }

        /// <summary>
        /// How much time to get ready before leaving
        /// </summary>
        public TimeSpan  TimeToPrepare { get; set; }
    }
}
