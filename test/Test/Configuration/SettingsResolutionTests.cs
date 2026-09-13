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
    /// falling back to the literal environment variable names.
    /// </summary>
    public class SettingsResolutionTests
    {
        private static ServiceProvider Build(Dictionary<string, string?>? settings = null)
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
        public void TransportOpenDataApiKey_FallsBackToLiteralEnvironmentVariable()
        {
            using var provider = Build();

            var expected = Environment.GetEnvironmentVariable("TRANSPORTOPENDATA__APIKEY") ?? string.Empty;
            Assert.Equal(expected, provider.GetRequiredService<IOptions<TransportOpenDataConfig>>().Value.ApiKey);
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

        [Fact]
        public void SlackConnector_ResolveAppToken_WithoutOptions_UsesLiteralEnvironmentVariable()
        {
            var connector = new SlackConnector(NullLogger<SlackConnector>.Instance);

            Assert.Equal(Environment.GetEnvironmentVariable("AWTRIXSHARP_SLACK__APPTOKEN"), connector.ResolveAppToken());
        }
    }
}
