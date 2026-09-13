using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Test
{
    /// <summary>
    /// Validates the application's DI graph (minus MVC/Swagger) so interface seams can't
    /// silently break at runtime.
    /// </summary>
    public class CompositionRootTests
    {
        private static ServiceProvider BuildProvider()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOptions();
            services.AddSingleton<IHostEnvironment>(Mock.Of<IHostEnvironment>(e => e.EnvironmentName == "Production"));

            AwtrixSharpWeb.Program.AddAwtrixServices(services, new ConfigurationBuilder().Build());

            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        }

        [Fact]
        public void Graph_ValidatesOnBuild_AndConductorResolves()
        {
            using var provider = BuildProvider();

            Assert.NotNull(provider.GetRequiredService<Conductor>());
        }

        [Fact]
        public void InterfaceSeams_ResolveToTheSameSingletons()
        {
            using var provider = BuildProvider();

            Assert.Same(provider.GetRequiredService<TimerService>(), provider.GetRequiredService<ITimerService>());
            Assert.Same(provider.GetRequiredService<MqttConnector>(), provider.GetRequiredService<IMqttConnector>());
            Assert.Same(provider.GetRequiredService<SlackConnector>(), provider.GetRequiredService<ISlackConnector>());
            Assert.Same(provider.GetRequiredService<IAwtrixService>(), provider.GetRequiredService<IAwtrixService>());
            Assert.IsType<AwtrixService>(provider.GetRequiredService<IAwtrixService>());
            Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
            Assert.IsType<Clock>(provider.GetRequiredService<IClock>());
            Assert.NotNull(provider.GetRequiredService<ITripPlannerService>());
        }

        [Fact]
        public void HttpPublisherNamedClient_HasFiveSecondTimeout()
        {
            using var provider = BuildProvider();

            var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpPublisher.HttpClientName);

            Assert.Equal(TimeSpan.FromSeconds(5), client.Timeout);
        }

        [Fact]
        public void TripPlanner_IsASingleton_OverANamedClientWithTimeoutAndApiKeyHeader()
        {
            // CR-29: no transient service holding typed clients captured by the singleton Conductor
            using var provider = BuildProvider();

            Assert.Same(provider.GetRequiredService<TripPlannerService>(), provider.GetRequiredService<ITripPlannerService>());
            Assert.Same(provider.GetRequiredService<ITripPlannerService>(), provider.GetRequiredService<ITripPlannerService>());

            var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(TripPlannerService.HttpClientName);
            Assert.Equal(TimeSpan.FromSeconds(15), client.Timeout);
            Assert.StartsWith("apikey", Assert.Single(client.DefaultRequestHeaders.GetValues("Authorization")));
        }

        [Fact]
        public void RegistersFourHostedServicesInStartupOrder()
        {
            using var provider = BuildProvider();

            var hosted = provider.GetServices<IHostedService>().ToList();

            Assert.Collection(hosted,
                h => Assert.IsType<MqttConnector>(h),
                h => Assert.IsType<SlackConnector>(h),
                h => Assert.IsType<Conductor>(h),
                h => Assert.IsType<TimerService>(h));
        }
    }
}
