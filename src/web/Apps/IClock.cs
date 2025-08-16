
namespace AwtrixSharpWeb.Apps
{
    public interface IClock
    {
        DateTimeOffset Now { get; }
    }
}