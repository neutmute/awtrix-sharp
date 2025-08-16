using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;

namespace AwtrixSharpWeb.Apps
{
    public abstract class AwtrixApp
    {
        protected AwtrixAddress AwtrixAddress;
        protected IAwtrixService AwtrixService;

        protected ILogger Logger { get; private set; }

        public AwtrixApp(ILogger logger, AwtrixAddress awtrixAddress, IAwtrixService awtrixService)
        {
            AwtrixAddress = awtrixAddress;
            AwtrixService = awtrixService;
            Logger = logger;
        }

        public abstract void Initialize();
    }
}
