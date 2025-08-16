using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services;

namespace AwtrixSharpWeb.Apps
{
    public abstract class AwtrixApp
    {
        protected AwtrixAddress AwtrixAddress;
        protected AwtrixService AwtrixService;

        public AwtrixApp(AwtrixAddress awtrixAddress, AwtrixService awtrixService)
        {
            AwtrixAddress = awtrixAddress;
            AwtrixService = awtrixService;
        }

        public abstract void Initialize();
    }
}
