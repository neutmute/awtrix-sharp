using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using NCrontab;
using static System.Net.Mime.MediaTypeNames;

namespace AwtrixSharpWeb.Apps
{
    public abstract class ScheduledApp<TConfig> : AwtrixApp<TConfig>, IDisposable where TConfig : ScheduledAppConfig
    {
        protected CancellationTokenSource _cts;
        /// <summary>
        /// waiting to wakeup
        /// </summary>
        protected bool IsScheduled { get; private set; }
        protected CrontabSchedule CrontabSchedule { get; private set; }
        //protected readonly TConfig Config;
        protected IClock Clock { get; }



        public ScheduledApp(ILogger logger, IClock clock, AwtrixAddress awtrixAddress, IAwtrixService awtrixService, TConfig config) : base(logger, config, awtrixAddress, awtrixService) 
        {
          //  Config = config;
            Clock = clock;
        }

        protected override void Initialize()
        {
            CrontabSchedule = CrontabSchedule.Parse(Config.CronSchedule);

            ScheduleNextWakeUp();
        }

        protected abstract Task ActivateScheduledWork(CancellationTokenSource cts);

        public void Dispose()
        {
            Dispose(true);
        }

        protected void Dispose(bool disposing)
        {
            if (disposing)
            {

                Logger.LogInformation($"Disposing  App: {Config.Name}");

                _ = Dismiss();
                _ = AppClear();

                Dispose(_cts);
            }
        }

        private bool Dispose(CancellationTokenSource cts)
        {
            if (cts == null)
            {
                return false; // Nothing to dispose
            }
            cts.Cancel();
            cts.Dispose();


            return true;
        }

        private void ScheduleNextWakeUp()
        {
            if (IsScheduled)
            {
                return; // Already scheduled
            }

            IsScheduled = true;
            Dispose(_cts);
            _cts = new CancellationTokenSource();

            // Start a background task to wait for the next scheduled time
            Task.Run(async () =>
            {
                try
                {
                    await WaitForCronSchedule(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Normal during cancellation
                }
                catch (Exception ex)
                {
                    // Log exception if needed
                    Logger.LogWarning($"Error in TripTimerApp: {ex.Message}");
                }
            });
        }

        private async Task WaitForCronSchedule(CancellationToken cancellationToken)
        {
            // Wait until the next scheduled time
            var now = Clock.Now;
            var next = CrontabSchedule.GetNextOccurrence(now.DateTime);
            var delay = next - now;

            Logger.LogInformation($"{Config.Name} Next wake up scheduled for {next} (in {delay})");

            await Task.Delay(delay, cancellationToken);

            // Only invoke WakeUp if we weren't cancelled
            if (!cancellationToken.IsCancellationRequested)
            {
                Logger.LogInformation($"Waking up for {Config.ActiveTime}");
                _cts.CancelAfter(Config.ActiveTime);
                await WakeUp();
            }
        }

        public override void ExecuteNow()
        {
            Logger.LogInformation($"ExecuteNow() triggering immediate wake");
            Dispose(_cts);
            _cts = new CancellationTokenSource();
            _ = WakeUp();
        }

        private async Task WakeUp()
        {
            IsScheduled = false; // Reset the scheduled flag
            var _activationStartTime = Clock.Now;

            try
            {
                await AppClear();
                await ActivateScheduledWork(_cts);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error during wakeup {ex}", ex);
            }
            finally
            {
                var activeTime = Clock.Now - _activationStartTime;
                Logger.LogInformation($"{Config.Name} was active for {activeTime.TotalSeconds:F1} seconds. Dismissing notice");

                ScheduleNextWakeUp();
            }
        }

        protected static Task WaitForCancellation(CancellationToken token)
        {
            var tcs = new TaskCompletionSource();
            token.Register(() => tcs.TrySetResult());
            return tcs.Task;
        }

    }
}
