namespace AwtrixSharpWeb.Apps
{
    /// <summary>
    /// Runs asynchronous work items one at a time, in the order they were enqueued.
    /// <para>
    /// <see cref="Enqueue"/> never blocks: when the queue is idle the work starts synchronously on the
    /// caller's thread (and completes synchronously if the work does); otherwise it runs after the
    /// previous item finishes. A faulted or cancelled item does not stop later items; its exception is
    /// surfaced only through the task returned for that item. No lock is held while work runs.
    /// </para>
    /// </summary>
    public sealed class SerialWorkQueue
    {
        private readonly object _gate = new();
        private Task _tail = Task.CompletedTask;

        public Task Enqueue(Func<Task> work)
        {
            ArgumentNullException.ThrowIfNull(work);

            // The tail only ever completes successfully, so awaiting it never throws
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task previous;
            lock (_gate)
            {
                previous = _tail;
                _tail = done.Task;
            }

            return RunAsync(previous, work, done);
        }

        private static async Task RunAsync(Task previous, Func<Task> work, TaskCompletionSource done)
        {
            try
            {
                await previous.ConfigureAwait(false);
                await work().ConfigureAwait(false);
            }
            finally
            {
                done.SetResult();
            }
        }
    }
}
