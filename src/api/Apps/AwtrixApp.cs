using AwtrixSharpWeb.Apps.Configs;
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

        public readonly TConfig Config;

        public IAppConfig GetConfig() => Config;

        protected ILogger Logger { get; private set; }

        public AwtrixApp(ILogger logger, TConfig config, AwtrixAddress awtrixAddress, IAwtrixService awtrixService)
        {
            AwtrixAddress = awtrixAddress;
            AwtrixService = awtrixService;
            Config = config;
            Logger = logger;
        }

        public void Init()
        {
            _ = AppClear().Result;

            Logger.LogInformation("Initializing {Config} for {AwtrixAddress}", Config.Name, AwtrixAddress);

            Initialize();
        }

        protected abstract void Initialize();

        /// <summary>
        /// For debugging purposes, allow immediate execution of the app
        /// </summary>
        public virtual void ExecuteNow()
        {
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
    }
}
