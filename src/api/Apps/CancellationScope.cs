namespace AwtrixSharpWeb.Apps
{
    /// <summary>
    /// A CancellationTokenSource linked to a parent token, with an optional timeout on an injected TimeProvider.
    /// <see cref="Token"/> is captured at construction, so reading it never throws ObjectDisposedException, and
    /// <see cref="Cancel"/> is a no-op after <see cref="Dispose"/>, so a late canceller never throws either.
    /// </summary>
    internal sealed class CancellationScope : IDisposable
    {
        /// <summary>Largest delay a CancellationTokenSource timer accepts (about 49.7 days).</summary>
        internal static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

        private readonly object _sync = new();
        private readonly CancellationTokenSource _linked;
        private readonly CancellationTokenSource? _timeout;
        private bool _disposed;

        public CancellationScope(CancellationToken parent, TimeSpan? timeout = null, TimeProvider? timeProvider = null)
        {
            if (timeout is { } requested)
            {
                _timeout = new CancellationTokenSource(ClampTimeout(requested), timeProvider ?? TimeProvider.System);
                _linked = CancellationTokenSource.CreateLinkedTokenSource(parent, _timeout.Token);
            }
            else
            {
                _linked = CancellationTokenSource.CreateLinkedTokenSource(parent);
            }

            Token = _linked.Token;
        }

        public CancellationToken Token { get; }

        public bool IsCancellationRequested => Token.IsCancellationRequested;

        public void Cancel()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }
                _linked.Cancel();
            }
        }

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
            _linked.Dispose();
            _timeout?.Dispose();
        }

        internal static TimeSpan ClampTimeout(TimeSpan requested) =>
            requested <= TimeSpan.Zero ? TimeSpan.Zero : requested > MaxTimeout ? MaxTimeout : requested;
    }
}
