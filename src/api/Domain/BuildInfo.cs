using System.Reflection;

namespace AwtrixSharpWeb.Domain
{
    /// <summary>
    /// Version and git commit baked into the assembly at build time (see awtrix-api.csproj AssemblyMetadata and the
    /// GIT_COMMIT_SHORT / VERSION build args in the Dockerfile). Single source for the startup log and startup notification.
    /// </summary>
    public static class BuildInfo
    {
        private static readonly Assembly Assembly = typeof(BuildInfo).Assembly;

        /// <summary>Short git hash, or null for a local build without one.</summary>
        public static string? CommitShort { get; } = Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "GitCommitShort" && !string.IsNullOrWhiteSpace(a.Value))?.Value;

        /// <summary>Assembly version, e.g. "1.0.42.0" from the CI run number.</summary>
        public static string Version { get; } = Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

        /// <summary>"Awtrix Sharp {commit}", or "Awtrix Sharp v{version}" when no commit was baked in.</summary>
        public static string Describe(string? commitShort) =>
            string.IsNullOrWhiteSpace(commitShort) ? $"Awtrix Sharp v{Version}" : $"Awtrix Sharp {commitShort}";

        public static string Description => Describe(CommitShort);
    }
}
