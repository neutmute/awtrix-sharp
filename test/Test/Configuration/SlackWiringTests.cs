using AwtrixSharpWeb.Apps.SlackStatus;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using SlackNet;
using SlackNet.Events;

namespace Test.Configuration
{
    /// <summary>
    /// Production DI wiring carries Slack:UserId from IConfiguration through Conductor into SlackStatusApp (CR-14).
    /// The container is Program.AddAwtrixServices over in-memory configuration; only IAwtrixService and IMqttConnector
    /// are replaced by mocks, so nothing publishes or connects, and no hosted service (Slack socket, MQTT) is started.
    /// </summary>
    public class SlackWiringTests
    {
        [Fact]
        public async Task SlackUserId_FromConfiguration_ReachesSlackStatusApp_ThroughConductor()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Slack:UserId"] = "U-FROM-CONFIGURATION",
                    ["Awtrix:Devices:0:BaseTopic"] = "awtrix/wiring-test",
                    ["Awtrix:Devices:0:Apps:0:Type"] = AppNames.SlackStatusApp,
                })
                .Build();

            var published = new TaskCompletionSource<AwtrixAppMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var awtrix = new Mock<IAwtrixService>();
            awtrix.Setup(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()))
                .Callback<AwtrixAddress, string, AwtrixAppMessage>((_, _, message) => published.TrySetResult(message))
                .ReturnsAsync(true);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IHostEnvironment>(Mock.Of<IHostEnvironment>(e => e.EnvironmentName == "Production"));
            services.Configure<AwtrixConfig>(configuration.GetSection("Awtrix")); // as Program.SetupConfiguration binds it
            AwtrixSharpWeb.Program.AddAwtrixServices(services, configuration);
            services.AddSingleton(awtrix.Object);                          // last registration wins: no publisher
            services.AddSingleton(new Mock<IMqttConnector>().Object);      // no MQTT client

            await using var provider = services.BuildServiceProvider();
            var conductor = provider.GetRequiredService<Conductor>();
            var slackConnector = provider.GetRequiredService<SlackConnector>();

            await conductor.StartAsync(CancellationToken.None);
            try
            {
                Assert.IsType<SlackStatusApp>(Assert.Single(conductor.FindApps(AppNames.SlackStatusApp)));

                // A user_change for the configured user, delivered the way SlackNet delivers it
                await slackConnector.Handle(new UserChange
                {
                    User = new User { Id = "U-FROM-CONFIGURATION", Profile = new UserProfile { StatusText = "In a meeting" } },
                });

                var message = await published.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal("In a meeting", message.Text);
            }
            finally
            {
                await conductor.StopAsync(CancellationToken.None);
            }
        }
    }
}
