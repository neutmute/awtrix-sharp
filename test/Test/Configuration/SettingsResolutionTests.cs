using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TransportOpenData;

namespace Test.Configuration
{
    /// <summary>
    /// CR-14: secrets and settings come from IConfiguration (appsettings, user secrets, AWTRIXSHARP_ provider),
    /// falling back to the literal environment variable names (see <see cref="SettingsEnvironmentFallbackTests"/>).
    /// </summary>
    public class SettingsResolutionTests
    {
        internal static ServiceProvider Build(Dictionary<string, string?>? settings = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOptions();
            services.AddSingleton<IHostEnvironment>(Mock.Of<IHostEnvironment>(e => e.EnvironmentName == "Production"));

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
                .Build();

            AwtrixSharpWeb.Program.AddAwtrixServices(services, configuration);
            return services.BuildServiceProvider();
        }

        [Fact]
        public void TransportOpenDataApiKey_IsReadFromConfiguration()
        {
            using var provider = Build(new() { ["TransportOpenData:ApiKey"] = "key-from-config" });

            Assert.Equal("key-from-config", provider.GetRequiredService<IOptions<TransportOpenDataConfig>>().Value.ApiKey);
        }

        [Fact]
        public void TransportOpenDataBaseUrl_DefaultsToTripPlannerUrl_AndCanBeOverridden()
        {
            using (var provider = Build())
            {
                Assert.Equal("https://api.transport.nsw.gov.au/v1/tp",
                    provider.GetRequiredService<IOptions<TransportOpenDataConfig>>().Value.BaseUrl);
            }

            using (var provider = Build(new() { ["TransportOpenData:BaseUrl"] = "https://example.test/tp" }))
            {
                Assert.Equal("https://example.test/tp",
                    provider.GetRequiredService<IOptions<TransportOpenDataConfig>>().Value.BaseUrl);
            }
        }

        [Fact]
        public void SlackSettings_AreReadFromConfiguration()
        {
            using var provider = Build(new()
            {
                ["Slack:AppToken"] = "xapp-from-config",
                ["Slack:UserId"] = "U-FROM-CONFIG",
            });

            var slack = provider.GetRequiredService<IOptions<SlackSettings>>().Value;
            Assert.Equal("xapp-from-config", slack.AppToken);
            Assert.Equal("U-FROM-CONFIG", slack.UserId);
        }

        [Fact]
        public void DataSettings_AreReadFromSettingsDataDirectoryKey()
        {
            using var provider = Build(new() { ["Settings:DATA_DIRECTORY"] = "/data/awtrix" });

            Assert.Equal("/data/awtrix", provider.GetRequiredService<IOptions<DataSettings>>().Value.DataDirectory);
        }

        [Fact]
        public void SlackConnector_ResolveAppToken_UsesOptions()
        {
            var connector = new SlackConnector(
                NullLogger<SlackConnector>.Instance,
                Options.Create(new SlackSettings { AppToken = "xapp-from-options" }));

            Assert.Equal("xapp-from-options", connector.ResolveAppToken());
        }
    }

    /// <summary>
    /// CR-14 literal environment-variable fallbacks. Each test sets the variable itself, so deleting a fallback fails a
    /// test whatever the developer's environment holds: configuration absent + variable set uses the variable;
    /// configuration present + variable set keeps the configuration value.
    /// </summary>
    [Collection(ProcessEnvironmentCollection.Name)]
    public class SettingsEnvironmentFallbackTests
    {
        private const string ApiKeyVariable = AwtrixSharpWeb.Program.TransportOpenDataApiKeyEnvironmentVariable;

        [Fact]
        public void TransportOpenDataApiKey_ConfigAbsent_UsesLiteralEnvironmentVariable() =>
            ProcessEnvironmentCollection.WithVariable(ApiKeyVariable, "key-from-env", () =>
            {
                using var provider = SettingsResolutionTests.Build();

                Assert.Equal("key-from-env", provider.GetRequiredService<IOptions<TransportOpenDataConfig>>().Value.ApiKey);
            });

        [Fact]
        public void TransportOpenDataApiKey_ConfigPresent_WinsOverEnvironmentVariable() =>
            ProcessEnvironmentCollection.WithVariable(ApiKeyVariable, "key-from-env", () =>
            {
                using var provider = SettingsResolutionTests.Build(new() { ["TransportOpenData:ApiKey"] = "key-from-config" });

                Assert.Equal("key-from-config", provider.GetRequiredService<IOptions<TransportOpenDataConfig>>().Value.ApiKey);
            });

        [Fact]
        public void SlackSettings_ConfigAbsent_UseLiteralEnvironmentVariables() =>
            ProcessEnvironmentCollection.WithVariable(SlackSettings.AppTokenEnvironmentVariable, "xapp-from-env", () =>
            ProcessEnvironmentCollection.WithVariable(SlackSettings.UserIdEnvironmentVariable, "U-FROM-ENV", () =>
            {
                using var provider = SettingsResolutionTests.Build();

                var slack = provider.GetRequiredService<IOptions<SlackSettings>>().Value;
                Assert.Equal("xapp-from-env", slack.AppToken);
                Assert.Equal("U-FROM-ENV", slack.UserId);
            }));

        [Fact]
        public void SlackSettings_ConfigPresent_WinOverEnvironmentVariables() =>
            ProcessEnvironmentCollection.WithVariable(SlackSettings.AppTokenEnvironmentVariable, "xapp-from-env", () =>
            ProcessEnvironmentCollection.WithVariable(SlackSettings.UserIdEnvironmentVariable, "U-FROM-ENV", () =>
            {
                using var provider = SettingsResolutionTests.Build(new()
                {
                    ["Slack:AppToken"] = "xapp-from-config",
                    ["Slack:UserId"] = "U-FROM-CONFIG",
                });

                var slack = provider.GetRequiredService<IOptions<SlackSettings>>().Value;
                Assert.Equal("xapp-from-config", slack.AppToken);
                Assert.Equal("U-FROM-CONFIG", slack.UserId);
            }));

        [Fact]
        public void DataDirectory_ConfigAbsent_UsesEnvironmentVariable() =>
            ProcessEnvironmentCollection.WithVariable(DataSettings.DataDirectoryEnvironmentVariable, "/from-env", () =>
            {
                using var provider = SettingsResolutionTests.Build();

                Assert.Equal("/from-env", provider.GetRequiredService<IOptions<DataSettings>>().Value.DataDirectory);
            });

        [Fact]
        public void DataDirectory_ConfigPresent_WinsOverEnvironmentVariable() =>
            ProcessEnvironmentCollection.WithVariable(DataSettings.DataDirectoryEnvironmentVariable, "/from-env", () =>
            {
                using var provider = SettingsResolutionTests.Build(new() { ["Settings:DATA_DIRECTORY"] = "/from-config" });

                Assert.Equal("/from-config", provider.GetRequiredService<IOptions<DataSettings>>().Value.DataDirectory);
            });

        [Fact]
        public void SlackConnector_ResolveAppToken_WithoutOptions_UsesLiteralEnvironmentVariable() =>
            ProcessEnvironmentCollection.WithVariable(SlackSettings.AppTokenEnvironmentVariable, "xapp-from-env", () =>
            {
                var connector = new SlackConnector(NullLogger<SlackConnector>.Instance);

                Assert.Equal("xapp-from-env", connector.ResolveAppToken());
            });
    }
}
