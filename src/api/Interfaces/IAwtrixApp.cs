using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Domain;
using System.Runtime.CompilerServices;

namespace AwtrixSharpWeb.Interfaces
{
    public interface IAwtrixApp : IDisposable
    {
        public AwtrixAddress AwtrixAddress { get; }

        public IAppConfig GetConfig();

        void Init();

        void ExecuteNow();
    }
}