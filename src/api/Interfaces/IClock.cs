namespace AwtrixSharpWeb.Interfaces
{
    public interface IClock
    {
        DateTimeOffset Now { get; }
    }
}