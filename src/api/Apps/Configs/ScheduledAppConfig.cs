namespace AwtrixSharpWeb.Apps.Configs
{
    public class ScheduledAppConfig : AppConfig
    {
        public string CronSchedule { get; set; }

        /// <summary>
        /// How long to take over the clock for
        /// </summary>
        public TimeSpan ActiveTime { get; set; }
    }
}
