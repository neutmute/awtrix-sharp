using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SlackNet;
using SlackNet.Events;
using SlackNet.SocketMode;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AwtrixSharpWeb.HostedServices
{

    public class SlackConnector : IHostedService, IEventHandler<UserChange>
    {
        private readonly ILogger<SlackConnector> _logger;
        private Task? _executingTask;
        private CancellationTokenSource? _stoppingCts;
        private static ISlackApiClient _slackApiClient;
        private static ISlackSocketModeClient _slackSocketClient;

        public event EventHandler<SlackUserStatusChangedEventArgs>? UserStatusChanged;

        public event EventHandler<SlackDndChangedEventArgs>? UserDnChanged;

        public SlackConnector(ILogger<SlackConnector> logger)
        {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Starting Slack connector service");

            // Create a linked token source so we can cancel when the app is stopping
            _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Start the long-running task in the background
            _executingTask = ExecuteAsync(_stoppingCts.Token);

            // If the task is completed then return it, otherwise return a completed task
            return _executingTask.IsCompleted ? _executingTask : Task.CompletedTask;
        }

        private async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                var appToken = Environment.GetEnvironmentVariable("AWTRIXSHARP_SLACK__APPTOKEN"); // xapp-***

                if (string.IsNullOrEmpty(appToken))
                {
                    _logger.LogWarning("Slack AppToken not configured. Slack integration disabled.");
                    return;
                }

                _logger.LogInformation("Connecting to Slack");

                _slackSocketClient = new SlackServiceBuilder()
                                    .UseAppLevelToken(appToken)
                                    .RegisterEventHandler(this)
                                    .GetSocketModeClient();

                //_slackApiClient = new SlackServiceBuilder()
                //    .UseApiToken(appToken) // xoxp for user scopes, or xoxb with proper scopes
                //    .GetApiClient();


                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var socketModeOptions = new SocketModeConnectionOptions
                        {
                            DebugReconnects = false
                        };

                        await _slackSocketClient.Connect(socketModeOptions, stoppingToken);


                        await Task.Delay(Timeout.Infinite, stoppingToken);
                    }
                    catch (WebSocketException wsEx)
                    {
                        _logger.LogError(wsEx, "WebSocket error, reconnecting in 5 seconds...");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in Slack connection, reconnecting in 5 seconds...");
                    }

                    // Wait before reconnecting to avoid hammering the Slack API
                    if (!stoppingToken.IsCancellationRequested)
                    {
                        await Task.Delay(5000, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal during shutdown, just log and exit
                _logger.LogInformation("Slack connector stopping due to cancellation");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in Slack connector");
            }
            finally
            {
                _logger.LogInformation("Slack connector stopped");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Slack connector service");

            if (_executingTask == null)
            {
                return;
            }

            try
            {
                // Signal cancellation to the executing method
                _stoppingCts?.Cancel();
                _slackSocketClient.Disconnect();
            }
            finally
            {
                // Wait until the task completes or the stop token triggers
                // Use a timeout to avoid hanging indefinitely
                var completedTask = await Task.WhenAny(_executingTask, Task.Delay(5000, cancellationToken));

                if (completedTask != _executingTask)
                {
                    _logger.LogWarning("Slack connector service shutdown timed out");
                }
            }

            _logger.LogInformation("Slack connector service stopped");
        }

        public Task Handle(UserChange slackEvent)
        {
            try
            {
                var statusChangedEvent = new SlackUserStatusChangedEventArgs();

                statusChangedEvent.UserId = slackEvent.User.Id;
                statusChangedEvent.StatusText = slackEvent.User.Profile.StatusText;
                statusChangedEvent.StatusEmoji = slackEvent.User.Profile.StatusEmoji;

                _logger.LogInformation("User status change -> {statusChangedEvent}", statusChangedEvent);

                UserStatusChanged?.Invoke(this, statusChangedEvent); 
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling user_change event");
            }

            return Task.CompletedTask;
        }
    }

}