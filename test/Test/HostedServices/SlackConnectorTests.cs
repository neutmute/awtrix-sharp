using System.Reflection;
using AwtrixSharpWeb.HostedServices;
using Microsoft.Extensions.Logging.Abstractions;

namespace Test.HostedServices
{
    /// <summary>
    /// CR-16: stopping the host without Slack configured must not throw.
    /// </summary>
    public class SlackConnectorTests
    {
        private const BindingFlags AnyField = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        [Fact]
        public async Task StopAsync_WhenSlackWasNeverConnected_DoesNotThrow()
        {
            var connector = new SlackConnector(NullLogger<SlackConnector>.Instance);
            // State after StartAsync ran without AWTRIXSHARP_SLACK__APPTOKEN: the executing task
            // finished early and no socket client was created. Set directly so the test ignores the
            // developer's environment.
            typeof(SlackConnector).GetField("_executingTask", AnyField)!.SetValue(connector, Task.CompletedTask);
            typeof(SlackConnector).GetField("_slackSocketClient", AnyField)!.SetValue(connector, null);

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
    }
}
