using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;
using NCrontab;

namespace AwtrixSharpWeb.Apps
{
    public class Clock : IClock
    {
        public DateTimeOffset Now { get => DateTimeOffset.Now; }
    }

    public abstract class ScheduledApp<TConfig> : AwtrixApp, IDisposable where TConfig : ScheduledAppConfig
    {
        protected CancellationTokenSource _cts;
        /// <summary>
        /// waiting to wakeup
        /// </summary>
        protected bool IsScheduled { get; private set; }
        protected CrontabSchedule CrontabSchedule { get; private set; }
        protected readonly TConfig Config;
        protected IClock Clock { get; }



        public ScheduledApp(ILogger logger, IClock clock, AwtrixAddress awtrixAddress, IAwtrixService awtrixService, TConfig config) : base(logger, awtrixAddress, awtrixService) 
        {
            AwtrixAddress = awtrixAddress;
            Config = config;
            Clock = clock;
        }
        public override void Initialize()
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

                // Clear any displayed messages
                _ = AwtrixService.Dismiss(AwtrixAddress);

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
                    Console.WriteLine($"Error in TripTimerApp: {ex.Message}");
                }
            });
        }

        private async Task WaitForCronSchedule(CancellationToken cancellationToken)
        {
            // Wait until the next scheduled time
            var now = Clock.Now;
            var next = CrontabSchedule.GetNextOccurrence(now.DateTime);
            var delay = next - now;

            Logger.LogInformation($"Next wake up scheduled for {next} (in {delay})");

            await Task.Delay(delay, cancellationToken);

            // Only invoke WakeUp if we weren't cancelled
            if (!cancellationToken.IsCancellationRequested)
            {
                // Set up cancellation for when ActiveTime expires
                _cts.CancelAfter(Config.ActiveTime);
                await WakeUp();
            }
        }

        private async Task WakeUp()
        {
            IsScheduled = false; // Reset the scheduled flag
            var _activationStartTime = Clock.Now;

            try
            {
                await ActivateScheduledWork(_cts);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in WakeUp: {ex.Message}");
            }
            finally
            {
                var activeTime = Clock.Now - _activationStartTime;
                Logger.LogInformation($"{Config.Name} was active for {activeTime.TotalSeconds:F1} seconds");

                await AwtrixService.Dismiss(AwtrixAddress);
                ScheduleNextWakeUp();
            }
        }
    }


    public class TripTimerApp : ScheduledApp<TripTimerAppConfig>
    {
        private readonly TripPlannerService _tripPlanner;
        private readonly TimerService _timerService;

        public TripTimerApp(
            ILogger logger
            , IClock clock
            , AwtrixAddress awtrixAddress
            , IAwtrixService awtrixService
            , TimerService timerService
            , TripTimerAppConfig config
            , TripPlannerService tripPlanner) : base(logger, clock, awtrixAddress, awtrixService, config)
        {
            _tripPlanner = tripPlanner;
            _timerService = timerService;
        }

        public override void Initialize()
        {
            base.Initialize();
        }

        private void ClockTickMinute(object? sender, ClockTickEventArgs e)
        {
          
        }

        private void ClockTickSecond(object? sender, ClockTickEventArgs e)
        {
            // We could update a countdown display here if needed
        }

        protected override async Task ActivateScheduledWork(CancellationTokenSource cts)
        {
            _timerService.SecondChanged += ClockTickSecond;
            _timerService.MinuteChanged += ClockTickMinute;

            try
            {
                await WaitForCancellation(cts.Token);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in ActivateScheduledWork: {ex.Message}");
            }
            finally
            {

                _timerService.SecondChanged -= ClockTickSecond;
                _timerService.MinuteChanged -= ClockTickMinute;
            }
        }

        public static Task WaitForCancellation(CancellationToken token)
        {
            var tcs = new TaskCompletionSource();
            token.Register(() => tcs.TrySetResult());
            return tcs.Task;
        }


        // Clean up resources
        new protected void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Unsubscribe from events
                _timerService.SecondChanged -= ClockTickSecond;
                _timerService.MinuteChanged -= ClockTickMinute;
            }
            
            base.Dispose(disposing);
        }

        // Override the non-protected Dispose method to call our new protected Dispose method
        public new void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
