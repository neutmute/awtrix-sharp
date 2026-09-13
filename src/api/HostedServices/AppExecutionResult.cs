namespace AwtrixSharpWeb.HostedServices
{
    /// <summary>
    /// Outcome of <see cref="Conductor.ExecuteNow"/>.
    /// </summary>
    public enum AppExecutionResult
    {
        /// <summary>No running app of that Type on that device.</summary>
        NotFound,

        /// <summary>ExecuteNow was invoked on every matching app without throwing.</summary>
        Started,

        /// <summary>At least one matching app threw from ExecuteNow.</summary>
        Error
    }
}
