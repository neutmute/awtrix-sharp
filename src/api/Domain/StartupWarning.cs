namespace AwtrixSharpWeb.Domain
{
    /// <summary>A problem found while registering services that should be logged once at startup rather than thrown.</summary>
    public sealed record StartupWarning(string Message);
}
