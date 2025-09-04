using AwtrixSharpWeb.Domain;

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