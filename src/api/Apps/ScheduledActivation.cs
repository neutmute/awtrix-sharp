namespace AwtrixSharpWeb.Apps
{
    public enum ActivationTrigger
    {
        Cron,
        Manual
    }

    /// <summary>
    /// One window during which a ScheduledApp owns the clock. Its <see cref="Token"/> is cancelled when the app
    /// calls <see cref="Complete"/>, when ActiveTime elapses, when a newer activation supersedes it, or when the
    /// app is disposed. Background work started for the window (e.g. a refresh loop) should observe the token.
    /// </summary>
    public sealed class ScheduledActivation
    {
        private readonly CancellationScope _scope;
        private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <param name="onCallbackError">
        /// Receives exceptions thrown by callbacks registered on <see cref="Token"/>; ending the window never rethrows them.
        /// </param>
        internal ScheduledActivation(int number, ActivationTrigger trigger, DateTimeOffset startedAt, TimeSpan activeTime, TimeProvider timeProvider, CancellationToken lifetime, Action<AggregateException>? onCallbackError = null)
        {
            Number = number;
            Trigger = trigger;
            StartedAt = startedAt;
            ActiveTime = activeTime;
            _scope = new CancellationScope(lifetime, activeTime, timeProvider, onCallbackError);
            Token = _scope.Token;
            Token.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), _ended);
        }

        /// <summary>1-based sequence number within the app.</summary>
        public int Number { get; }

        public ActivationTrigger Trigger { get; }

        public DateTimeOffset StartedAt { get; }

        public TimeSpan ActiveTime { get; }

        public CancellationToken Token { get; }

        public bool IsEnded => Token.IsCancellationRequested;

        /// <summary>
        /// End this window early (e.g. nothing left to show). Idempotent, thread-safe, never throws, and never runs
        /// deactivation inline on the caller's thread.
        /// </summary>
        public void Complete() => _scope.Cancel();

        /// <summary>True once OnActivateAsync was called; only then does deactivation run.</summary>
        internal bool WasActivated { get; set; }

        /// <summary>Completes (asynchronously) when <see cref="Token"/> is cancelled. Never faults.</summary>
        internal Task Ended => _ended.Task;

        internal void Release() => _scope.Dispose();
    }
}
