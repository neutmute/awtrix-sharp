using AwtrixSharpWeb.Apps.MqttRender;
using AwtrixSharpWeb.Domain;
using System.Runtime.CompilerServices;

namespace AwtrixSharpWeb.Interfaces
{
    public interface IAwtrixApp : IDisposable
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