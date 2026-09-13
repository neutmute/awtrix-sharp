using System.Reflection;
using AwtrixSharpWeb.HostedServices;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SlackNet;

namespace Test.HostedServices
{
    /// <summary>
    /// CR-16: stopping the host without Slack configured must not throw.
    /// CR-41: connector state is per instance.
    /// </summary>
    public class SlackConnectorTests
    {
        private const BindingFlags InstanceField = BindingFlags.NonPublic | BindingFlags.Instance;

        private static void SetField(SlackConnector connector, string name, object? value)
        {
            var field = typeof(SlackConnector).GetField(name, InstanceField);
            Assert.NotNull(field); // CR-41: connector state is per instance, never static
            field!.SetValue(connector, value);
        }

        [Fact]
        public async Task StopAsync_WhenSlackWasNeverConnected_DoesNotThrow()
        {
            var connector = new SlackConnector(NullLogger<SlackConnector>.Instance);
            // State after StartAsync ran without an app token: the executing task finished early and
            // no socket client was created. Set directly so the test ignores the developer's environment.
            SetField(connector, "_executingTask", Task.CompletedTask);
            SetField(connector, "_slackSocketClient", null);

            var exception = await Record.ExceptionAsync(() => connector.StopAsync(CancellationToken.None));

            Assert.Null(exception);
        }

        [Fact]
        public async Task StopAsync_BeforeStart_DoesNotThrow()
        {
            var connector = new SlackConnector(NullLogger<SlackConnector>.Instance);

            var exception = await Record.ExceptionAsync(() => connector.StopAsync(CancellationToken.None));

            Assert.Null(exception);
        }

        [Fact]
        public async Task StopAsync_DoesNotDisconnectAnotherInstancesSocketClient()
        {
            var socket = new Mock<ISlackSocketModeClient>();
            var first = new SlackConnector(NullLogger<SlackConnector>.Instance);
            SetField(first, "_slackSocketClient", socket.Object);
            var second = new SlackConnector(NullLogger<SlackConnector>.Instance);
            SetField(second, "_executingTask", Task.CompletedTask);

            await second.StopAsync(CancellationToken.None);

            socket.Verify(s => s.Disconnect(), Times.Never);
        }

        [Fact]
        public void DeadMembers_AreRemoved()
        {
            Assert.Null(typeof(SlackConnector).GetField("_slackApiClient", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static));
            Assert.Null(typeof(SlackConnector).GetEvent("UserDnChanged"));
        }
    }
}
