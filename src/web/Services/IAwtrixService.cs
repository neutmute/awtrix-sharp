using AwtrixSharpWeb.Domain;

namespace AwtrixSharpWeb.Services
{
    public interface IAwtrixService
    {
        Task<bool> AppClear(AwtrixAddress awtrixAddress, string appName);
        Task<bool> AppUpdate(AwtrixAddress awtrixAddress, string appName, AwtrixAppMessage message);
        Task<bool> Dismiss(AwtrixAddress awtrixAddress);
        Task<bool> Notify(AwtrixAddress awtrixAddress, AwtrixAppMessage message);
    }
}