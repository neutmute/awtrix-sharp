namespace AwtrixSharpWeb.Apps.Configs
{
    public class ScheduledAppConfig : AppConfig
    {
        public string CronSchedule 
        { 
            get => GetConfig<string>("CronSchedule");
            set => SetConfig("CronSchedule", value);
        }

        /// <summary>
        /// How long to take over the clock for
        /// </summary>
        public TimeSpan ActiveTime
        { 
            get => GetConfig<TimeSpan>("ActiveTime");
            set => SetConfig("ActiveTime", value);
        }
    }
}
