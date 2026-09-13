using AwtrixSharpWeb.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AwtrixSharpWeb.HostedServices
{
    public class ClockTickEventArgs : EventArgs
    {
        /// <summary>
        /// Local wall-clock time of the tick, truncated to the whole second (Kind = Local).
        /// </summary>
        public DateTime Time { get; }

        public ClockTickEventArgs(DateTime currentTime)
        {
            Time = currentTime;
        }
    }

    /// <summary>
    /// Raises SecondChanged / MinuteChanged from a single sequential PeriodicTimer loop.
    /// Subscribers are invoked one at a time, each isolated in its own try/catch, so a
    /// throwing subscriber can neither crash the process nor starve other subscribers.
    /// Subscribers must return quickly; offload I/O (see AwtrixApp.FireAndLog).
    /// </summary>
    public class TimerService : IHostedService, IDisposable, ITimerService
    {
        internal static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);

        private readonly ILogger<TimerService> _logger;
        private readonly TimeProvider _timeProvider;
        private DateTime _lastSecond;
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

        public TimerService(ILogger<TimerService> logger, TimeProvider? timeProvider = null)
        {
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _lastSecond = CurrentLocalSecond();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _executingTask = ExecuteAsync(_stoppingCts.Token);
            return _executingTask.IsCompleted ? _executingTask : Task.CompletedTask;
        }

        private async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogDebug("Timer service executing");
            using var timer = new PeriodicTimer(TickInterval, _timeProvider);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    Tick();
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Timer service stopping due to cancellation");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Timer loop terminated unexpectedly");
            }
            finally
            {
                _logger.LogInformation("Timer service stopped");
            }
        }

        /// <summary>
        /// One timer iteration. Never throws.
        /// </summary>
        internal void Tick()
        {
            try
            {
                var current = CurrentLocalSecond();
                if (current == _lastSecond)
                {
                    return;
                }

                var previous = _lastSecond;
                // Update before invoking handlers so a slow/throwing handler can't cause a duplicate
                _lastSecond = current;

                _logger.LogDebug("Second changed: {Second}", current.ToString("HH:mm:ss"));
                Raise(SecondChanged, current, nameof(SecondChanged));

                if (TruncateToMinute(current) != TruncateToMinute(previous))
                {
                    _logger.LogDebug("Minute changed: {Minute}", current.ToString("HH:mm:ss"));
                    Raise(MinuteChanged, current, nameof(MinuteChanged));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Timer tick failed");
            }
        }

        private void Raise(EventHandler<ClockTickEventArgs>? handlers, DateTime time, string eventName)
        {
            if (handlers == null)
            {
                return;
            }

            var args = new ClockTickEventArgs(time);
            foreach (EventHandler<ClockTickEventArgs> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, args);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "{Event} subscriber {Subscriber} threw; continuing with remaining subscribers",
                        eventName,
                        $"{handler.Method.DeclaringType?.Name}.{handler.Method.Name}");
                }
            }
        }

        private DateTime CurrentLocalSecond()
        {
            var local = _timeProvider.GetLocalNow().DateTime;
            return new DateTime(local.Ticks - (local.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Local);
        }

        private static DateTime TruncateToMinute(DateTime time)
        {
            return new DateTime(time.Ticks - (time.Ticks % TimeSpan.TicksPerMinute), time.Kind);
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
                _stoppingCts?.Cancel();
            }
            finally
            {
                var completedTask = await Task.WhenAny(_executingTask, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));

                if (completedTask != _executingTask)
                {
                    _logger.LogWarning("Timer service shutdown timed out");
                }
            }
        }

        public void Dispose()
        {
            _stoppingCts?.Dispose();
        }

        public static string FormatClockString(DateTime time, bool format24h)
        {
            var thisSecond = time.Second;
            var isOddSecond = thisSecond % 2 == 1;
            var spacer = isOddSecond ? " " : ":";

            var hourString = time.ToString(format24h ? "HH" : "hh");
            if (!format24h)
            {
                hourString = hourString.TrimStart('0');
            }

            var clockText = $"{hourString}{spacer}{time:mm}";

            return clockText;
        }
    }
}
