using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using NCrontab;

namespace AwtrixSharpWeb.Apps
{
    /// <summary>
    /// Base for apps that take over the clock for Config.ActiveTime, on a cron schedule or on demand (ExecuteNow).
    /// <para>
    /// State, all mutated under <see cref="_gate"/>: at most one pending cron wait and at most one current
    /// activation; nothing is armed while an activation is current; nothing re-arms or activates after disposal.
    /// Activations are serialised: a superseded activation's teardown (OnDeactivateAsync + AppClear) completes
    /// before the next activation's OnActivateAsync runs.
    /// </para>
    /// </summary>
    public abstract class ScheduledApp<TConfig> : AwtrixApp<TConfig> where TConfig : ScheduledAppConfig
    {
        /// <summary>Longest single timer used while waiting for a cron occurrence (Task.Delay rejects > ~49.7 days).</summary>
        internal static readonly TimeSpan MaxDelayChunk = TimeSpan.FromDays(1);

        /// <summary>Back-off before retrying after a failure while waiting for the schedule.</summary>
        internal static readonly TimeSpan WaitRetryDelay = TimeSpan.FromMinutes(1);

        /// <summary>Budget for OnDeactivateAsync, and for disposal waiting on the last run's teardown.</summary>
        internal static readonly TimeSpan DeactivationTimeout = TimeSpan.FromSeconds(5);

        private readonly object _gate = new();
        private readonly CancellationTokenSource _lifetime = new(); // cancelled on dispose; never disposed (no timer)
        private CancellationScope? _pendingWait;
        private ScheduledActivation? _active;
        private ScheduledActivation? _currentActivation;
        private Task _lastRun = Task.CompletedTask;
        private DateTimeOffset? _nextWakeUp;
        private int _activationCount;
        private bool _disposed;

        public ScheduledApp(ILogger logger, IClock clock, AwtrixAddress awtrixAddress, IAwtrixService awtrixService, TConfig config)
            : base(logger, config, awtrixAddress, awtrixService)
        {
            Clock = clock;
        }

        protected IClock Clock { get; }

        protected CrontabSchedule? CrontabSchedule { get; private set; }

        /// <summary>
        /// The activation whose OnActivateAsync ran and whose OnDeactivateAsync has not finished, or null.
        /// Event handlers guard with <c>if (CurrentActivation is not { IsEnded: false }) return;</c>.
        /// </summary>
        protected ScheduledActivation? CurrentActivation => Volatile.Read(ref _currentActivation);

        /// <summary>When the pending cron wait will activate the app; null while active, unscheduled or disposed.</summary>
        internal DateTimeOffset? NextWakeUp
        {
            get { lock (_gate) { return _nextWakeUp; } }
        }

        /// <summary>Completes when the latest activation's teardown and re-arm have finished. Never faults.</summary>
        internal Task LastRun
        {
            get { lock (_gate) { return _lastRun; } }
        }

        private TimeProvider Time => Clock.TimeProvider;

        /// <summary>
        /// Wire up the window (subscribe, fetch) and return once wired; the base waits for the window to end.
        /// Observe <see cref="ScheduledActivation.Token"/> in anything that can take a while.
        /// </summary>
        protected abstract Task OnActivateAsync(ScheduledActivation activation);

        /// <summary>
        /// Undo OnActivateAsync (unsubscribe). Runs once per activation that was activated, even if activation threw.
        /// The base clears the app slot afterwards. Bounded by <see cref="DeactivationTimeout"/>.
        /// </summary>
        protected virtual Task OnDeactivateAsync(ScheduledActivation activation) => Task.CompletedTask;

        internal static TimeSpan NextDelayChunk(TimeSpan remaining) => remaining < MaxDelayChunk ? remaining : MaxDelayChunk;

        protected override void Initialize()
        {
            CrontabSchedule = CrontabSchedule.Parse(Config.CronSchedule);
            ArmNextWait();
        }

        public override void ExecuteNow()
        {
            Logger.LogInformation("ExecuteNow: activating {App} on {AwtrixAddress}", Config.Name, AwtrixAddress);
            StartActivation(ActivationTrigger.Manual, fromWait: null);
        }

        private void ArmNextWait()
        {
            CancellationScope wait;
            lock (_gate)
            {
                if (_disposed || CrontabSchedule == null || _active != null || _pendingWait != null)
                {
                    return;
                }
                wait = new CancellationScope(_lifetime.Token);
                _pendingWait = wait;
            }

            // Runs synchronously up to its first timer, so the wait is armed when this returns.
            _ = WaitThenActivateAsync(wait);
        }

        private async Task WaitThenActivateAsync(CancellationScope wait)
        {
            try
            {
                while (true)
                {
                    try
                    {
                        var due = GetNextOccurrence();
                        lock (_gate)
                        {
                            if (ReferenceEquals(_pendingWait, wait))
                            {
                                _nextWakeUp = due;
                            }
                        }
                        Logger.LogInformation("{App} next wake up scheduled for {Due} (in {Delay})", Config.Name, due, due - Time.GetUtcNow());

                        await DelayUntilAsync(due, wait.Token);
                        break;
                    }
                    catch (OperationCanceledException) when (wait.IsCancellationRequested)
                    {
                        return; // superseded by ExecuteNow, or disposed
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "{App} failed while waiting for its schedule; retrying in {Delay}", Config.Name, WaitRetryDelay);
                        try
                        {
                            await Task.Delay(WaitRetryDelay, Time, wait.Token).ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                    }
                }

                StartActivation(ActivationTrigger.Cron, wait);
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_pendingWait, wait))
                    {
                        _pendingWait = null;
                        _nextWakeUp = null;
                    }
                }
                wait.Dispose();
            }
        }

        /// <summary>Next cron occurrence strictly after now, in the time provider's local zone (host-local in production).</summary>
        private DateTimeOffset GetNextOccurrence()
        {
            var now = Time.GetLocalNow();
            var next = CrontabSchedule!.GetNextOccurrence(now.DateTime);
            return new DateTimeOffset(next, Time.LocalTimeZone.GetUtcOffset(next));
        }

        private async Task DelayUntilAsync(DateTimeOffset due, CancellationToken token)
        {
            while (true)
            {
                var remaining = due - Time.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    return;
                }

                // ForceYielding: a cancelling thread (tick handler, Dispose) never runs this continuation inline
                await Task.Delay(NextDelayChunk(remaining), Time, token).ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            }
        }

        private void StartActivation(ActivationTrigger trigger, CancellationScope? fromWait)
        {
            var activeTime = ReadActiveTime();

            ScheduledActivation activation;
            ScheduledActivation? superseded;
            CancellationScope? pendingWait;
            Task previousRun;
            TaskCompletionSource runCompleted;
            lock (_gate)
            {
                if (_disposed)
                {
                    Logger.LogDebug("{App} is disposed; ignoring {Trigger} activation", Config.Name, trigger);
                    return;
                }
                if (fromWait != null && !ReferenceEquals(_pendingWait, fromWait))
                {
                    return; // this wait was superseded while it was waking up
                }

                superseded = _active;
                pendingWait = _pendingWait;
                _pendingWait = null;
                _nextWakeUp = null;

                activation = new ScheduledActivation(++_activationCount, trigger, Time.GetLocalNow(), activeTime, Time, _lifetime.Token);
                _active = activation;

                previousRun = _lastRun;
                runCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _lastRun = runCompleted.Task;
            }

            if (pendingWait != null && !ReferenceEquals(pendingWait, fromWait))
            {
                pendingWait.Cancel();
            }
            if (superseded != null)
            {
                Logger.LogInformation("{App} activation #{Old} superseded by #{New} ({Trigger})", Config.Name, superseded.Number, activation.Number, trigger);
                superseded.Complete();
            }

            _ = RunActivationAsync(activation, previousRun, runCompleted);
        }

        private TimeSpan ReadActiveTime()
        {
            try
            {
                var activeTime = Config.ActiveTime;
                if (activeTime <= TimeSpan.Zero)
                {
                    Logger.LogWarning("{App} ActiveTime is {ActiveTime}; the activation ends immediately", Config.Name, activeTime);
                }
                return activeTime;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{App} has a missing or invalid ActiveTime; the activation ends immediately", Config.Name);
                return TimeSpan.Zero;
            }
        }

        private async Task RunActivationAsync(ScheduledActivation activation, Task previousRun, TaskCompletionSource runCompleted)
        {
            try
            {
                // Serialise: the previous activation's teardown finishes before this one wires anything up.
                await previousRun;
                if (activation.IsEnded)
                {
                    return; // superseded while waiting, ActiveTime <= 0, or disposed
                }

                Logger.LogInformation("{App} activation #{Number} ({Trigger}) starting for {ActiveTime}", Config.Name, activation.Number, activation.Trigger, activation.ActiveTime);
                activation.WasActivated = true;
                Volatile.Write(ref _currentActivation, activation);

                await AppClear();

                // ForceYielding: if a superseding ExecuteNow (controller or MQTT receive thread) or Dispose cancels a
                // token OnActivateAsync is awaiting, this run's teardown resumes on the pool, never on that thread.
                await OnActivateAsync(activation).ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
                await activation.Ended;
            }
            catch (OperationCanceledException) when (activation.IsEnded)
            {
                Logger.LogDebug("{App} activation #{Number} ended while activating", Config.Name, activation.Number);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{App} activation #{Number} failed", Config.Name, activation.Number);
            }
            finally
            {
                await EndActivationAsync(activation, runCompleted);
            }
        }

        private async Task EndActivationAsync(ScheduledActivation activation, TaskCompletionSource runCompleted)
        {
            activation.Complete(); // OnActivateAsync may have thrown: end the window so its timeout and handlers stop

            if (activation.WasActivated)
            {
                try
                {
                    await OnDeactivateAsync(activation).WaitAsync(DeactivationTimeout, Time);
                }
                catch (TimeoutException)
                {
                    Logger.LogWarning("{App} activation #{Number} did not deactivate within {Timeout}", Config.Name, activation.Number, DeactivationTimeout);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "{App} activation #{Number} failed to deactivate", Config.Name, activation.Number);
                }

                Interlocked.CompareExchange(ref _currentActivation, null, activation);

                try
                {
                    await AppClear();
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "{App} could not clear its slot after activation #{Number}", Config.Name, activation.Number);
                }

                Logger.LogInformation("{App} activation #{Number} ended after {Seconds:F1} s", Config.Name, activation.Number, (Time.GetLocalNow() - activation.StartedAt).TotalSeconds);
            }

            bool rearm;
            lock (_gate)
            {
                rearm = ReferenceEquals(_active, activation);
                if (rearm)
                {
                    _active = null;
                }
                rearm = rearm && !_disposed;
            }

            activation.Release();
            if (rearm)
            {
                ArmNextWait();
            }
            runCompleted.TrySetResult(); // last, so awaiting LastRun also observes the re-armed wait
        }

        protected override void ReleaseResources()
        {
            Logger.LogInformation("Disposing app {App}", Config.Name);
            lock (_gate)
            {
                _disposed = true;
                _nextWakeUp = null;
            }
            _lifetime.Cancel(); // ends the pending wait and the current activation; teardown runs on the thread pool
            base.ReleaseResources();
        }

        protected override async Task DisposeCoreAsync()
        {
            try
            {
                // WS3 review M4: await the ended activation's teardown (bounded) before the final clear
                await LastRun.WaitAsync(DeactivationTimeout, Time);
            }
            catch (TimeoutException)
            {
                Logger.LogWarning("{App} did not finish deactivating within {Timeout}; clearing anyway", Config.Name, DeactivationTimeout);
            }

            await base.DisposeCoreAsync();
        }
    }
}
