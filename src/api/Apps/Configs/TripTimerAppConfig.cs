namespace AwtrixSharpWeb.Apps.Configs
{
    public class MqttAppConfig : ScheduledAppConfig
    {
        public string Icon { get; set; } = string.Empty;
        public string ReadTopic { get; set; } = string.Empty;
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
