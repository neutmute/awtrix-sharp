using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace Test.Configuration
{
    /// <summary>
    /// The Simulator environment is the only one the app may be run in locally: it must not load user secrets
    /// (which hold the real broker and clock) and must target only the NG simulator on localhost.
    /// </summary>
    public class SimulatorEnvironmentTests : IDisposable
    {
        private readonly List<WebApplicationBuilder> _builders = new();
        private static readonly string ApiProjectDir = ResolveApiProjectDir();

        private static string ResolveApiProjectDir()
        {
            var candidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "api"));
            if (File.Exists(Path.Combine(candidate, "awtrix-api.csproj")))
            {
                return candidate;
            }

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var probe = Path.Combine(dir.FullName, "src", "api", "awtrix-api.csproj");
                if (File.Exists(probe))
                {
                    return Path.Combine(dir.FullName, "src", "api");
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException($"Could not locate src/api/awtrix-api.csproj starting from {AppContext.BaseDirectory}");
        }

        private WebApplicationBuilder CreateBuilder(string environment)
        {
            Assert.True(File.Exists(Path.Combine(ApiProjectDir, "awtrix-api.csproj")), $"ApiProjectDir did not resolve to src/api: {ApiProjectDir}");

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = ApiProjectDir,
                EnvironmentName = environment,
                ApplicationName = "awtrix-api",
            });
            AwtrixSharpWeb.Program.SetupConfiguration(builder.Configuration, builder.Services, environment);
            _builders.Add(builder);
            return builder;
        }

        [Fact]
        public void Simulator_DoesNotLoadUserSecrets()
        {
            var builder = CreateBuilder(AwtrixSharpWeb.Program.SimulatorEnvironmentName);

            var sources = ((IConfigurationBuilder)builder.Configuration).Sources;
            Assert.DoesNotContain(sources, s => s is JsonConfigurationSource json && json.Path != null && json.Path.EndsWith("secrets.json", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Development_LoadsUserSecrets_SoTheGuardIsMeaningful()
        {
            var builder = CreateBuilder("Development");

            var sources = ((IConfigurationBuilder)builder.Configuration).Sources;
            Assert.Contains(sources, s => s is JsonConfigurationSource json && json.Path != null && json.Path.EndsWith("secrets.json", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Simulator_LoadsSimulatorSettingsFile()
        {
            var builder = CreateBuilder(AwtrixSharpWeb.Program.SimulatorEnvironmentName);

            var sources = ((IConfigurationBuilder)builder.Configuration).Sources.OfType<JsonConfigurationSource>().Select(s => s.Path).ToList();
            Assert.Contains("appsettings.Simulator.json", sources);
        }

        [Fact]
        public void Simulator_TargetsOnlyTheLocalSimulator()
        {
            const string name = "AWTRIXSHARP_MQTT__HOST";
            var previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, "real-broker.invalid");
            try
            {
                var builder = CreateBuilder(AwtrixSharpWeb.Program.SimulatorEnvironmentName);
                var config = builder.Configuration.GetSection("Awtrix").Get<AwtrixSharpWeb.Domain.AwtrixConfig>()!;

                var device = Assert.Single(config.Devices);
                Assert.Equal("http://localhost:8080", device.BaseTopic);
                Assert.Equal(AwtrixSharpWeb.Services.Firmware.AwtrixFirmwareKind.NG, device.Firmware);
                Assert.Equal("localhost", builder.Configuration["Mqtt:Host"]);
            }
            finally
            {
                Environment.SetEnvironmentVariable(name, previous);
            }
        }

        [Fact]
        public void Production_StillLoadsAwtrixSharpPrefixedVariables()
        {
            const string name = "AWTRIXSHARP_MQTT__HOST";
            var previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, "real-broker.invalid");
            try
            {
                var builder = CreateBuilder("Production");

                Assert.Equal("real-broker.invalid", builder.Configuration["Mqtt:Host"]);
            }
            finally
            {
                Environment.SetEnvironmentVariable(name, previous);
            }
        }

        public void Dispose()
        {
            foreach (var builder in _builders)
            {
                (builder.Configuration as IDisposable)?.Dispose();
            }
        }
    }
}
