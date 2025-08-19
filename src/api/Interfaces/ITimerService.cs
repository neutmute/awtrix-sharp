using AwtrixSharpWeb.HostedServices;

namespace AwtrixSharpWeb.Interfaces
{
    public interface ITimerService
    {
        event EventHandler<ClockTickEventArgs>? MinuteChanged;
        event EventHandler<ClockTickEventArgs>? SecondChanged;

        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
    }
}