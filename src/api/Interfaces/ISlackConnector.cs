using AwtrixSharpWeb.HostedServices;

namespace AwtrixSharpWeb.Interfaces
{
    public interface ISlackConnector
    {
        event EventHandler<SlackUserStatusChangedEventArgs>? UserStatusChanged;
    }
}
