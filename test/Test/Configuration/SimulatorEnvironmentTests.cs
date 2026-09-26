using AwtrixSharpWeb.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text.Json;
using TransportOpenData;

namespace Test.Configuration
{
    /// <summary>
    /// The Simulator environment is the only one the app may be run in locally: it must not load user secrets
    /// (which hold the real broker and clock) and must target only the NG simulator on localhost.
    /// Tests that set AWTRIXSHARP_MQTT__HOST run in the non-parallel process-environment collection.
    /// </summary>
    [Collection(ProcessEnvironmentCollection.Name)]
    public class SimulatorEnvironmentTests : IDisposable
    {
        private const string MqttHostVariable = "AWTRIXSHARP_MQTT__HOST";
        private const string SlackAppTokenVariable = SlackSettings.AppTokenEnvironmentVariable;
        private const string SlackUserIdVariable = SlackSettings.UserIdEnvironmentVariable;
        private const string TransportOpenDataApiKeyVariable = AwtrixSharpWeb.Program.TransportOpenDataApiKeyEnvironmentVariable;
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
        public void Simulator_TargetsOnlyTheLocalSimulator() =>
            ProcessEnvironmentCollection.WithVariable(MqttHostVariable, "real-broker.invalid", () =>
            {
                var builder = CreateBuilder(AwtrixSharpWeb.Program.SimulatorEnvironmentName);
                var config = builder.Configuration.GetSection("Awtrix").Get<AwtrixSharpWeb.Domain.AwtrixConfig>()!;

                var device = Assert.Single(config.Devices);
                Assert.Equal("http://localhost:8080", device.BaseTopic);
                Assert.Equal("NG", device.Firmware);
                Assert.Equal(new[] { "DiurnalApp", "MqttClockRenderApp" }, device.Apps.Select(a => a.Type).ToArray());
                Assert.Equal("localhost", builder.Configuration["Mqtt:Host"]);
                Assert.DoesNotContain(JsonSourcePaths(builder), path => string.Equals(path, "appsettings.json", StringComparison.OrdinalIgnoreCase));
            });

        [Fact]
        public void Production_StillLoadsAwtrixSharpPrefixedVariables() =>
            ProcessEnvironmentCollection.WithVariable(MqttHostVariable, "real-broker.invalid", () =>
            {
                var builder = CreateBuilder("Production");

                Assert.Equal("real-broker.invalid", builder.Configuration["Mqtt:Host"]);
                Assert.Contains(JsonSourcePaths(builder), path => string.Equals(path, "appsettings.json", StringComparison.OrdinalIgnoreCase));
            });

        [Fact]
        public void Simulator_IgnoresSlackEnvironmentFallback() =>
            ProcessEnvironmentCollection.WithVariable(SlackAppTokenVariable, "xapp-test", () =>
            ProcessEnvironmentCollection.WithVariable(SlackUserIdVariable, "U-TEST", () =>
            {
                var builder = CreateBuilder(AwtrixSharpWeb.Program.SimulatorEnvironmentName);
                builder.Services.AddLogging();
                AwtrixSharpWeb.Program.AddAwtrixServices(builder.Services, builder.Configuration, environmentName: "Simulator");

                using var provider = builder.Services.BuildServiceProvider();
                var slack = provider.GetRequiredService<IOptions<SlackSettings>>().Value;

                Assert.True(string.IsNullOrEmpty(slack.AppToken));
                Assert.True(string.IsNullOrEmpty(slack.UserId));
            }));

        [Fact]
        public void Production_StillUsesSlackEnvironmentFallback() =>
            ProcessEnvironmentCollection.WithVariable(SlackAppTokenVariable, "xapp-test", () =>
            ProcessEnvironmentCollection.WithVariable(SlackUserIdVariable, "U-TEST", () =>
            {
                var builder = CreateBuilder("Production");
                builder.Services.AddLogging();
                AwtrixSharpWeb.Program.AddAwtrixServices(builder.Services, builder.Configuration, environmentName: "Production");

                using var provider = builder.Services.BuildServiceProvider();
                var slack = provider.GetRequiredService<IOptions<SlackSettings>>().Value;

                Assert.Equal("xapp-test", slack.AppToken);
                Assert.Equal("U-TEST", slack.UserId);
            }));

        [Fact]
        public void Simulator_IgnoresTransportOpenDataApiKeyFallback() =>
            ProcessEnvironmentCollection.WithVariable(TransportOpenDataApiKeyVariable, "key-test", () =>
            {
                var builder = CreateBuilder(AwtrixSharpWeb.Program.SimulatorEnvironmentName);
                builder.Services.AddLogging();
                AwtrixSharpWeb.Program.AddAwtrixServices(builder.Services, builder.Configuration, environmentName: "Simulator");

                using var provider = builder.Services.BuildServiceProvider();
                var apiKey = provider.GetRequiredService<IOptions<TransportOpenDataConfig>>().Value.ApiKey;

                Assert.True(string.IsNullOrEmpty(apiKey));
            });

        [Fact]
        public void Production_StillUsesTransportOpenDataApiKeyFallback() =>
            ProcessEnvironmentCollection.WithVariable(TransportOpenDataApiKeyVariable, "key-test", () =>
            {
                var builder = CreateBuilder("Production");
                builder.Services.AddLogging();
                AwtrixSharpWeb.Program.AddAwtrixServices(builder.Services, builder.Configuration, environmentName: "Production");

                using var provider = builder.Services.BuildServiceProvider();
                var apiKey = provider.GetRequiredService<IOptions<TransportOpenDataConfig>>().Value.ApiKey;

                Assert.Equal("key-test", apiKey);
            });

        [Fact]
        public void LaunchSettings_HasSimulatorProfilePinnedToSimulatorEnvironment()
        {
            var path = Path.Combine(ApiProjectDir, "Properties", "launchSettings.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

            var profile = doc.RootElement.GetProperty("profiles").GetProperty(AwtrixSharpWeb.Program.SimulatorEnvironmentName);

            Assert.Equal("Simulator", profile.GetProperty("environmentVariables").GetProperty("ASPNETCORE_ENVIRONMENT").GetString());
            Assert.Equal("Project", profile.GetProperty("commandName").GetString());
        }

        private static List<string?> JsonSourcePaths(WebApplicationBuilder builder) =>
            ((IConfigurationBuilder)builder.Configuration).Sources.OfType<JsonConfigurationSource>().Select(s => s.Path).ToList();

        public void Dispose()
        {
            foreach (var builder in _builders)
            {
                (builder.Configuration as IDisposable)?.Dispose();
            }
        }
    }
}
