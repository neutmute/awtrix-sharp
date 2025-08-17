using AwtrixSharpWeb.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AwtrixSharpWeb.HostedServices
{
    public class ClockTickEventArgs : EventArgs
    {
        /// <summary>
        /// Current DateTime when the minute changed
        /// </summary>
        public DateTime Time { get; }

        public ClockTickEventArgs(DateTime currentTime)
        {
            Time = currentTime;
        }
    }

    public class TimerService : IHostedService, IDisposable, ITimerService
    {
        private readonly ILogger<TimerService> _logger;
        private Timer? _timer;
        private DateTime _lastTime;
        private Task? _executingTask;
        private CancellationTokenSource? _stoppingCts;

        /// <summary>
        /// Event that fires every second
        /// </summary>
        public event EventHandler<ClockTickEventArgs>? SecondChanged;

        /// <summary>
        /// Event that fires every minute
        /// </summary>
        public event EventHandler<ClockTickEventArgs>? MinuteChanged;

        public TimerService(ILogger<TimerService> logger)
        {
            _logger = logger;
            _lastTime = DateTime.Now;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Timer service starting");

            // Create a linked token source so we can cancel when the app is stopping
            _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Start the timer in the background
            _executingTask = ExecuteAsync(_stoppingCts.Token);

            // If the task is completed then return it, otherwise return a completed task
            return _executingTask.IsCompleted ? _executingTask : Task.CompletedTask;
        }

        private async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("Timer service executing");

                // Create a timer that fires every 100ms to check for second changes
                // This gives us good granularity to detect second changes without missing any
                _timer = new Timer(CheckTimeChange, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));

                // Keep the service running until cancellation is requested
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal during shutdown, just log and exit
                _logger.LogInformation("Timer service stopping due to cancellation");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in timer service");
            }
            finally
            {
                _logger.LogInformation("Timer service stopped");
            }
        }

        private void CheckTimeChange(object? state)
        {
            var currentTime = DateTime.Now;

            // Check if second has changed
            if (currentTime.Second != _lastTime.Second)
            {
                _logger.LogDebug("Second changed: {Second}", currentTime.ToString("HH:mm:ss"));
                OnSecondChanged(currentTime);

                // Check if minute has changed as well
                if (currentTime.Minute != _lastTime.Minute)
                {
                    _logger.LogDebug("Minute changed: {Minute}", currentTime.ToString("HH:mm:ss"));
                    OnMinuteChanged(currentTime);
                }

                // Update last time
                _lastTime = currentTime;
            }
        }

        private void OnSecondChanged(DateTime currentTime)
        {
            SecondChanged?.Invoke(this, new ClockTickEventArgs(currentTime));
        }

        private void OnMinuteChanged(DateTime currentTime)
        {
            MinuteChanged?.Invoke(this, new ClockTickEventArgs(currentTime));
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping timer service");

            if (_executingTask == null)
            {
                return;
            }

            try
            {
                // Signal cancellation to the executing method
                _stoppingCts?.Cancel();
            }
            finally
            {
                // Stop the timer
                _timer?.Change(Timeout.Infinite, 0);

                // Wait until the task completes or the stop token triggers
                // Use a timeout to avoid hanging indefinitely
                var completedTask = await Task.WhenAny(_executingTask, Task.Delay(5000, cancellationToken));

                if (completedTask != _executingTask)
                {
                    _logger.LogWarning("Timer service shutdown timed out");
                }
            }

            _logger.LogInformation("Timer service stopped");
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _stoppingCts?.Dispose();
        }
    }
}
