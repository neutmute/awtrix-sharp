namespace Test
{
    /// <summary>
    /// Serialises tests that set process-wide environment variables which production code reads as fallbacks
    /// (AWTRIXSHARP_SLACK__*, TRANSPORTOPENDATA__APIKEY, AWTRIXSHARP_SETTINGS__DATA_DIRECTORY).
    /// DisableParallelization makes xUnit run this collection on its own, after the parallel collections.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class ProcessEnvironmentCollection
    {
        public const string Name = "ProcessEnvironment";

        /// <summary>Sets (or, for null, clears) <paramref name="name"/> while <paramref name="body"/> runs, then restores it.</summary>
        public static async Task WithVariable(string name, string? value, Func<Task> body)
        {
            var previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
            try
            {
                await body();
            }
            finally
            {
                Environment.SetEnvironmentVariable(name, previous);
            }
        }

        /// <inheritdoc cref="WithVariable(string, string?, Func{Task})"/>
        public static void WithVariable(string name, string? value, Action body)
        {
            WithVariable(name, value, () =>
            {
                body();
                return Task.CompletedTask;
            }).GetAwaiter().GetResult();
        }
    }
}
