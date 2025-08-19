using AwtrixSharpWeb.Interfaces;

namespace AwtrixSharpWeb.Domain
{
    public class Clock : IClock
    {
        public DateTimeOffset Now { get => DateTimeOffset.Now; }
    }
}
