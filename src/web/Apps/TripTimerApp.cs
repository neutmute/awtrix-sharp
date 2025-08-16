using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;
using NCrontab;

namespace AwtrixSharpWeb.Apps
{
    public abstract class ScheduledApp<TConfig> : AwtrixApp, IDisposable where TConfig : ScheduledAppConfig
    {
        protected CancellationTokenSource _cts;
        private bool _isScheduled = false;
        protected CrontabSchedule CrontabSchedule { get; private set; }
        protected readonly AwtrixAddress AwtrixAddress;
        protected readonly AwtrixService AwtrixService;
        protected readonly TConfig Config;

        public ScheduledApp(AwtrixAddress awtrixAddress, AwtrixService awtrixService, TConfig config) : base(awtrixAddress, awtrixService) 
        {
            AwtrixAddress = awtrixAddress;
            AwtrixService = awtrixService;
            Config = config;
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
                _cts?.Cancel();
                _cts?.Dispose();
            }
        }

        private void ScheduleNextWakeUp()
        {
            if (_isScheduled)
            {
                return; // Already scheduled
            }

            _isScheduled = true;
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
            var now = DateTime.Now;
            var next = CrontabSchedule.GetNextOccurrence(now);
            var delay = next - now;

            Console.WriteLine($"Next wake up scheduled for {next} (in {delay})");

            await Task.Delay(delay, cancellationToken);

            // Only invoke WakeUp if we weren't cancelled
            if (!cancellationToken.IsCancellationRequested)
            {
                await WakeUp();
            }
        }

        private async Task WakeUp()
        {
            _isScheduled = false; // Reset the scheduled flag

            try
            {
                await ActivateScheduledWork(_cts);
            }
            catch (Exception ex)
            {
                // Log exception if needed
                Console.WriteLine($"Error in WakeUp: {ex.Message}");
            }
            finally
            {
                // Schedule the next run
                ScheduleNextWakeUp();
            }
        }
    }


    public class TripTimerApp : ScheduledApp<TripTimerAppConfig>
    {
        private readonly TripPlannerService _tripPlanner;
        private readonly TripTimerAppConfig _config;
        private readonly TimerService _timerService;

        CancellationTokenSource _cts;

        public TripTimerApp(
            AwtrixAddress awtrixAddress
            , AwtrixService awtrixService
            , TimerService timerService
            , TripTimerAppConfig config
            , TripPlannerService tripPlanner) : base(awtrixAddress, awtrixService, config)
        {
            _tripPlanner = tripPlanner;
            _config = config;
            _timerService = timerService;
        }

        public override void Initialize()
        {
            base.Initialize();
            _timerService.SecondChanged += ClockTickSecond;
            _timerService.MinuteChanged += ClockTickMinute; 
        }

        private void ClockTickMinute(object? sender, ClockTickEventArgs e)
        {

        }

        private void ClockTickSecond(object? sender, ClockTickEventArgs e)
        {
        }

        protected override Task ActivateScheduledWork(CancellationTokenSource cts)
        {
            _cts = cts;
            return Task.CompletedTask;
        }
    }
}
