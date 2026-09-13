using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;

namespace AwtrixSharpWeb.Apps
{
    public abstract class AwtrixApp<TConfig> : IAwtrixApp where TConfig : AppConfig
    {
        public AwtrixAddress AwtrixAddress { get; private set; }

        private IAwtrixService AwtrixService;
        private int _initState;
        private int _disposeState;


        public readonly TConfig Config;

        public IAppConfig GetConfig() => Config;

        protected ILogger Logger { get; private set; }

        public AwtrixApp(ILogger logger, TConfig config, AwtrixAddress awtrixAddress, IAwtrixService awtrixService)
        {
            AwtrixAddress = awtrixAddress;
            AwtrixService = awtrixService;
            Config = config;
            Logger = logger;

            // CR-34: report bad ValueMap keys, values and regexes once, when the app is created
            config?.LogValueMapProblems(logger, awtrixAddress?.BaseTopic);
        }

        /// <summary>
        /// Runs at most once. A second call (or a call after a failed first attempt) logs and returns.
        /// </summary>
        public async Task InitAsync()
        {
            if (Interlocked.Exchange(ref _initState, 1) == 1)
            {
                Logger.LogWarning("InitAsync called more than once for {AppType} on {AwtrixAddress}; ignoring", Config.Type, AwtrixAddress);
                return;
            }

            await AppClear();

            Logger.LogInformation("Initializing {Config} for {AwtrixAddress}", Config.Type, AwtrixAddress);

            Initialize();
        }

        protected abstract void Initialize();

        /// <summary>
        /// For debugging purposes, allow immediate execution of the app
        /// </summary>
        public virtual void ExecuteNow()
        {
        }

        /// <summary>
        /// Run async work from a synchronous event handler (e.g. a clock tick) without blocking
        /// the caller and without ever letting an exception escape. The returned task never faults;
        /// callers normally discard it.
        /// </summary>
        protected Task FireAndLog(Func<Task> work, string operation)
        {
            return RunAndLogAsync(work, operation);
        }

        private async Task RunAndLogAsync(Func<Task> work, string operation)
        {
            try
            {
                await work();
            }
            catch (OperationCanceledException)
            {
                Logger.LogDebug("{Operation} cancelled for {AppType} on {AwtrixAddress}", operation, Config.Type, AwtrixAddress);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{Operation} failed for {AppType} on {AwtrixAddress}", operation, Config.Type, AwtrixAddress);
            }
        }

        protected async Task<bool> Notify(AwtrixAppMessage message)
        {
            return await AwtrixService.Notify(AwtrixAddress, message);
        }

        protected async Task<bool> Dismiss()
        {
            return await AwtrixService.Dismiss(AwtrixAddress);
        }

        protected async Task<bool> AppUpdate(AwtrixAppMessage message)
        {
            return await AwtrixService.AppUpdate(AwtrixAddress, Config.Name, message);
        }

        protected async Task<bool> AppClear()
        {
            if (Config.Name == null)
            {
                // Diurnal sending empty custom payload causes errors
                return false;
            }
            return await AwtrixService.AppClear(AwtrixAddress, Config.Name);
        }
        protected async Task<bool> Set(AwtrixSettings settings)
        {
            return await AwtrixService.Set(AwtrixAddress, settings);
        }

        /// <summary>
        /// Conductor's shutdown path. Runs once: <see cref="ReleaseResources"/> (synchronous: cancel work,
        /// unsubscribe), then awaits <see cref="DisposeCoreAsync"/> (final clears). Later calls return at once.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (!TryBeginDispose())
            {
                return;
            }

            ReleaseResources();
            await DisposeCoreAsync();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// For callers that cannot await (tests, using blocks). Runs once and never blocks on the network:
        /// the final clears are started through FireAndLog and not awaited. Conductor uses DisposeAsync.
        /// </summary>
        public void Dispose()
        {
            if (!TryBeginDispose())
            {
                return;
            }

            ReleaseResources();
            _ = FireAndLog(DisposeCoreAsync, nameof(Dispose));
            GC.SuppressFinalize(this);
        }

        protected bool IsDisposed => Volatile.Read(ref _disposeState) != 0;

        /// <summary>
        /// Synchronous, non-blocking release: cancel background work and unsubscribe from events.
        /// Called at most once. Overrides call base.
        /// </summary>
        protected virtual void ReleaseResources()
        {
        }

        /// <summary>
        /// Final publishes after <see cref="ReleaseResources"/>. The default clears this app's custom slot.
        /// Never dismisses notifications: that would remove other apps' notifications too (CR-31).
        /// Overrides call base last.
        /// </summary>
        protected virtual Task DisposeCoreAsync() => AppClear();

        private bool TryBeginDispose() => Interlocked.Exchange(ref _disposeState, 1) == 0;
    }
}
