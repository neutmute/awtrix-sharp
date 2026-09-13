using NCrontab;

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

        public override IReadOnlyList<string> Validate()
        {
            var errors = new List<string>(base.Validate());

            var cron = Config.Get("CronSchedule");
            if (string.IsNullOrWhiteSpace(cron))
            {
                errors.Add("CronSchedule: required value is missing (expected a 5-field cron expression, e.g. '10 6 * * 1-5')");
            }
            else if (CrontabSchedule.TryParse(cron) is null)
            {
                // Same parse options as ScheduledApp.Initialize (5-field)
                errors.Add($"CronSchedule: '{cron}' is not a valid 5-field cron expression");
            }

            ValidateTimeSpan(errors, "ActiveTime", required: true, mustBePositive: true);

            return errors;
        }
    }
}
