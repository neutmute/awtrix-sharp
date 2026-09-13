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
        /// <summary>
        /// The current activation's (or pending cron wait's) CTS. Replaced only under <see cref="_ctsLock"/>;
        /// each activation captures its own instance rather than re-reading this field.
        /// </summary>
        protected CancellationTokenSource _cts;
        private readonly object _ctsLock = new();
        private bool _disposed;
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

            ScheduleNextWakeUp(owner: null);
        }

        protected abstract Task ActivateScheduledWork(CancellationTokenSource cts);

        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Cancels any active run (its finally block deactivates) and awaits the final clears so they
        /// are published before the MQTT connector stops.
        /// </summary>
        public override async ValueTask DisposeAsync()
        {
            Logger.LogInformation("Disposing app {App}", Config.Name);

            CancellationTokenSource? current;
            lock (_ctsLock)
            {
                _disposed = true;
                current = _cts;
                _cts = null!;
            }
            CancelAndDispose(current);

            await Dismiss();
            await AppClear();
        }

        protected void Dispose(bool disposing)
        {
            if (disposing)
            {

                Logger.LogInformation($"Disposing  App: {Config.Name}");

                _ = Dismiss();
                _ = AppClear();

                CancellationTokenSource? current;
                lock (_ctsLock)
                {
                    _disposed = true;
                    current = _cts;
                    _cts = null!;
                }
                CancelAndDispose(current);
            }
        }

        /// <summary>
        /// Only the code that swapped a CTS out of <see cref="_cts"/> (under <see cref="_ctsLock"/>) cancels and
        /// disposes it, so each instance is torn down exactly once and never by a superseded activation.
        /// </summary>
        private static void CancelAndDispose(CancellationTokenSource? cts)
        {
            if (cts == null)
            {
                return; // Nothing to dispose
            }
            cts.Cancel();
            cts.Dispose();
        }

        /// <param name="owner">
        /// The CTS of the activation that just ended (null at start-up). A superseded activation — its CTS is no
        /// longer current because ExecuteNow replaced it — must not touch the newer activation's CTS.
        /// </param>
        private void ScheduleNextWakeUp(CancellationTokenSource? owner)
        {
            CancellationTokenSource previous;
            CancellationTokenSource next;
            lock (_ctsLock)
            {
                if (_disposed || !ReferenceEquals(_cts, owner) || IsScheduled)
                {
                    return; // Disposed, superseded by a newer activation, or already scheduled
                }

                IsScheduled = true;
                previous = _cts;
                next = new CancellationTokenSource();
                _cts = next;
            }
            CancelAndDispose(previous);

            // Start a background task to wait for the next scheduled time
            Task.Run(async () =>
            {
                try
                {
                    await WaitForCronSchedule(next);
                }
                catch (OperationCanceledException)
                {
                    // Normal during cancellation
                }
                catch (ObjectDisposedException)
                {
                    // Superseded by ExecuteNow or Dispose while waking up
                }
                catch (Exception ex)
                {
                    // Log exception if needed
                    Logger.LogWarning($"Error in TripTimerApp: {ex.Message}");
                }
            });
        }

        private async Task WaitForCronSchedule(CancellationTokenSource cts)
        {
            var cancellationToken = cts.Token;

            // Wait until the next scheduled time
            var now = Clock.Now;
            var next = CrontabSchedule.GetNextOccurrence(now.DateTime);
            var delay = next - now;

            Logger.LogInformation($"{Config.Name} Next wake up scheduled for {next} (in {delay})");

            await Task.Delay(delay, cancellationToken);

            lock (_ctsLock)
            {
                // Only invoke WakeUp if we weren't cancelled or superseded
                if (cancellationToken.IsCancellationRequested || !ReferenceEquals(_cts, cts))
                {
                    return;
                }
                Logger.LogInformation($"Waking up for {Config.ActiveTime}");
                cts.CancelAfter(Config.ActiveTime);
                IsScheduled = false; // Reset the scheduled flag
            }
            await WakeUp(cts);
        }

        public override void ExecuteNow()
        {
            Logger.LogInformation($"ExecuteNow() triggering immediate wake");
            CancellationTokenSource previous;
            var cts = new CancellationTokenSource();
            lock (_ctsLock)
            {
                if (_disposed)
                {
                    cts.Dispose();
                    return;
                }
                previous = _cts;
                _cts = cts;
                IsScheduled = false; // Reset the scheduled flag
            }
            CancelAndDispose(previous);
            _ = WakeUp(cts);
        }

        private async Task WakeUp(CancellationTokenSource cts)
        {
            var _activationStartTime = Clock.Now;

            try
            {
                await AppClear();
                await ActivateScheduledWork(cts);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error during wakeup {ex}", ex);
            }
            finally
            {
                var activeTime = Clock.Now - _activationStartTime;
                Logger.LogInformation($"{Config.Name} was active for {activeTime.TotalSeconds:F1} seconds. Dismissing notice");

                ScheduleNextWakeUp(owner: cts);
            }
        }

        protected static Task WaitForCancellation(CancellationToken token)
        {
            // RunContinuationsAsynchronously: Cancel() is called from tick handlers on the TimerService loop;
            // the awaiting continuation (deactivation I/O) must never run inline on that thread.
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetResult());
            return tcs.Task;
        }

    }
}
