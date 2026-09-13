using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Interfaces
{
    /// <summary>
    /// An app driving one Awtrix device. Lifecycle, owned by Conductor:
    /// <list type="number">
    /// <item>Construct: no publishing, subscribing or timers.</item>
    /// <item><see cref="InitAsync"/>: at most once, after every app for every device is constructed; may run concurrently with other apps.</item>
    /// <item><see cref="ExecuteNow"/>: zero or more times, from controller threads or the MQTT receive thread.</item>
    /// <item><see cref="IAsyncDisposable.DisposeAsync"/>: exactly once (also after a failed init), before the MQTT connector
    /// stops, abandoned after Conductor.AppDisposeTimeout. Conductor never calls the synchronous Dispose.</item>
    /// </list>
    /// </summary>
    public interface IAwtrixApp : IDisposable, IAsyncDisposable
    {
        public AwtrixAddress AwtrixAddress { get; }

        public IAppConfig GetConfig();

        /// <summary>
        /// Clears the app's slot and wires its subscriptions/schedule. Must not block on the network
        /// beyond the publisher timeouts.
        /// </summary>
        Task InitAsync();

        void ExecuteNow();
    }
}
