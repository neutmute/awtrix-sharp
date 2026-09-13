using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Services
{
    /// <summary>
    /// Device operations. Implementations never throw; false means "not delivered".
    /// </summary>
    public interface IAwtrixService
    {
        Task<bool> AppClear(AwtrixAddress awtrixAddress, string appName);
        Task<bool> AppUpdate(AwtrixAddress awtrixAddress, string appName, AwtrixAppMessage message);
        Task<bool> Dismiss(AwtrixAddress awtrixAddress);
        Task<bool> Notify(AwtrixAddress awtrixAddress, AwtrixAppMessage message);
        Task<bool> Set(AwtrixAddress awtrixAddress, AwtrixSettings settings);
        Task<bool> PlayRtttl(AwtrixAddress awtrixAddress, string rtttl);
    }
}
