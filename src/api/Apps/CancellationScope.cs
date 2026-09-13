namespace AwtrixSharpWeb.Apps
{
    /// <summary>
    /// A cancellation source that follows a parent token, with an optional timeout on an injected TimeProvider.
    /// <see cref="Token"/> is captured at construction, so reading it never throws ObjectDisposedException, and
    /// <see cref="Cancel"/> is a no-op after <see cref="Dispose"/>, so a late canceller never throws either.
    /// <para>
    /// Cancellation never throws, whichever way it arrives (<see cref="Cancel"/>, the parent, or the timeout timer):
    /// an exception from a callback registered on <see cref="Token"/> is reported to <c>onCallbackError</c> and
    /// swallowed (WS4 review m1). A linked CancellationTokenSource would instead rethrow it into the canceller:
    /// the engine's supersede/teardown path, the parent's Cancel, or a timer thread.
    /// </para>
    /// </summary>
    internal sealed class CancellationScope : IDisposable
    {
        /// <summary>Largest delay a CancellationTokenSource timer accepts (about 49.7 days).</summary>
        internal static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

        private readonly object _sync = new();
        private readonly CancellationTokenSource _source = new();
        private readonly CancellationTokenSource? _timeout;
        private readonly Action<AggregateException>? _onCallbackError;
        private readonly CancellationTokenRegistration _parentRegistration;
        private readonly CancellationTokenRegistration _timeoutRegistration;
        private bool _disposed;

        public CancellationScope(
            CancellationToken parent,
            TimeSpan? timeout = null,
            TimeProvider? timeProvider = null,
            Action<AggregateException>? onCallbackError = null)
        {
            _onCallbackError = onCallbackError;
            Token = _source.Token;

            // Registering on an already-cancelled token runs the callback inline, which is safe here
            _parentRegistration = parent.UnsafeRegister(static state => ((CancellationScope)state!).CancelSafely(), this);

            if (timeout is { } requested)
            {
                _timeout = new CancellationTokenSource(ClampTimeout(requested), timeProvider ?? TimeProvider.System);
                _timeoutRegistration = _timeout.Token.UnsafeRegister(static state => ((CancellationScope)state!).CancelSafely(), this);
            }
        }

        public CancellationToken Token { get; }

        public bool IsCancellationRequested => Token.IsCancellationRequested;

        public void Cancel() => CancelSafely();

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
            }
            _parentRegistration.Dispose();
            _timeoutRegistration.Dispose();
            _source.Dispose();
            _timeout?.Dispose();
        }

        internal static TimeSpan ClampTimeout(TimeSpan requested) =>
            requested <= TimeSpan.Zero ? TimeSpan.Zero : requested > MaxTimeout ? MaxTimeout : requested;

        private void CancelSafely()
        {
            AggregateException? callbackFailure = null;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    _source.Cancel(); // runs every callback, then throws their exceptions as one AggregateException
                }
                catch (AggregateException ex)
                {
                    callbackFailure = ex;
                }
            }

            if (callbackFailure != null)
            {
                try
                {
                    _onCallbackError?.Invoke(callbackFailure);
                }
                catch
                {
                    // Reporting must not re-open the hole this closes
                }
            }
        }
    }
}
