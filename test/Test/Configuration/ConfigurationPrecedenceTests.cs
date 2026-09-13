using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace Test.Configuration
{
    /// <summary>
    /// CR-13: SetupConfiguration used to re-add appsettings.json after every default source, so it
    /// overrode user secrets, appsettings.{Env}.json, plain env vars and the command line.
    /// </summary>
    public class ConfigurationPrecedenceTests : IDisposable
    {
        private readonly string _contentRoot;
        private readonly List<WebApplicationBuilder> _builders = new();

        public ConfigurationPrecedenceTests()
        {
            _contentRoot = Path.Combine(Path.GetTempPath(), "awtrixsharp-ws7-config-" + Guid.NewGuid());
            Directory.CreateDirectory(_contentRoot);
            File.WriteAllText(Path.Combine(_contentRoot, "appsettings.json"), """
                {
                  "Awtrix": { "Devices": [ { "BaseTopic": "from-appsettings" } ] },
                  "Ws7Test": { "Value": "from-appsettings", "EnvFile": "from-appsettings", "Prefixed": "from-appsettings" }
                }
                """);
            File.WriteAllText(Path.Combine(_contentRoot, "appsettings.Development.json"), """
                { "Ws7Test": { "EnvFile": "from-development-json" } }
                """);
        }

        private WebApplicationBuilder CreateBuilder(string environment, params string[] args)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = _contentRoot,
                EnvironmentName = environment,
            });
            AwtrixSharpWeb.Program.SetupConfiguration(builder.Configuration, builder.Services);
            _builders.Add(builder);
            return builder;
        }

        [Fact]
        public void AppsettingsJson_IsStillLoaded()
        {
            var builder = CreateBuilder("Production");

            Assert.Equal("from-appsettings", builder.Configuration["Ws7Test:Value"]);
        }

        [Fact]
        public void CommandLine_OverridesAppsettingsJson()
        {
            var builder = CreateBuilder("Production", "--Awtrix:Devices:0:BaseTopic=from-command-line");

            Assert.Equal("from-command-line", builder.Configuration["Awtrix:Devices:0:BaseTopic"]);
        }

        [Fact]
        public void EnvironmentSpecificJson_OverridesAppsettingsJson()
        {
            var builder = CreateBuilder("Development");

            Assert.Equal("from-development-json", builder.Configuration["Ws7Test:EnvFile"]);
        }

        [Fact]
        public void AwtrixSharpPrefixedEnvironmentVariable_StillWinsOverCommandLine()
        {
            const string name = "AWTRIXSHARP_WS7TEST__PREFIXED";
            var previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, "from-prefixed-env");
            try
            {
                var builder = CreateBuilder("Production", "--Ws7Test:Prefixed=from-command-line");

                Assert.Equal("from-prefixed-env", builder.Configuration["Ws7Test:Prefixed"]);
            }
            finally
            {
                Environment.SetEnvironmentVariable(name, previous);
            }
        }

        [Fact]
        public void AppsettingsJson_IsRegisteredExactlyOnce()
        {
            var builder = CreateBuilder("Production");

            var count = ((IConfigurationBuilder)builder.Configuration).Sources
                .OfType<JsonConfigurationSource>()
                .Count(s => string.Equals(s.Path, "appsettings.json", StringComparison.OrdinalIgnoreCase));

            Assert.Equal(1, count);
        }

        public void Dispose()
        {
            foreach (var builder in _builders)
            {
                (builder.Configuration as IDisposable)?.Dispose();
            }

            try
            {
                Directory.Delete(_contentRoot, recursive: true);
            }
            catch (IOException)
            {
                // A file watcher may still hold the directory on Windows; the temp folder is disposable.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
