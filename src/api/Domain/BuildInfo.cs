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

        /// <summary>Major.Minor.Build, e.g. "1.0.42" where Build is the CI run number.</summary>
        public static string Version { get; } = Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        /// <summary>"Awtrix-Sharp {version} {commit}", or just "Awtrix-Sharp {version}" when no commit was baked in.</summary>
        public static string Describe(string? commitShort) =>
            string.IsNullOrWhiteSpace(commitShort) ? $"Awtrix-Sharp {Version}" : $"Awtrix-Sharp {Version} {commitShort}";

        public static string Description => Describe(CommitShort);
    }
}
