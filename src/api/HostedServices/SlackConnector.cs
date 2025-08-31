using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AwtrixSharpWeb.HostedServices
{
    public class SlackUserEventArgs : EventArgs
    {
        public string UserId { get; set; } = string.Empty;


        public override string ToString()
        {
            return $"UserId={UserId}";
        }
    }
    public class SlackDndChangedEventArgs : SlackUserEventArgs
    {
        public bool IsDoNotDisturbEnabled { get; set; }

        public override string ToString()
        {
            return $"{base.ToString()}: IsDoNotDisturb={IsDoNotDisturbEnabled}";
        }
    }

    public class SlackUserStatusChangedEventArgs : SlackUserEventArgs
    {
        public string Name { get; set; } = string.Empty;

        public string StatusText { get; set; } = string.Empty;

        public string StatusEmoji { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{base.ToString()}, Name={Name}: {StatusEmoji} {StatusText}";
        }
    }

    public class SlackConnector : IHostedService
    {
        private readonly ILogger<SlackConnector> _logger;
        private static readonly HttpClient http = new HttpClient();
        private Task? _executingTask;
        private CancellationTokenSource? _stoppingCts;

        public event EventHandler<SlackUserStatusChangedEventArgs>? UserStatusChanged;

        public event EventHandler<SlackDndChangedEventArgs>? UserDnChanged;

        public SlackConnector(ILogger<SlackConnector> logger)
        {
            _logger = logger;
        }

        static async Task<string> OpenSocketUrlAsync(string appToken, CancellationToken cancellationToken)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "https://slack.com/api/apps.connections.open");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appToken);
            var res = await http.SendAsync(req, cancellationToken);
            var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cancellationToken)).RootElement;
            if (!json.GetProperty("ok").GetBoolean())
                throw new Exception($"apps.connections.open failed: {json.GetProperty("error").GetString()}");
            return json.GetProperty("url").GetString()!;
        }

        static async Task AckAsync(ClientWebSocket ws, string envelopeId, CancellationToken cancellationToken)
        {
            var ack = JsonSerializer.Serialize(new { envelope_id = envelopeId });
            await ws.SendAsync(Encoding.UTF8.GetBytes(ack), WebSocketMessageType.Text, true, cancellationToken);
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

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await ConnectAndProcessEventsAsync(appToken, stoppingToken);
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

        private async Task ConnectAndProcessEventsAsync(string appToken, CancellationToken stoppingToken)
        {
            var wsUrl = await OpenSocketUrlAsync(appToken, stoppingToken);

            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(wsUrl), stoppingToken);
            _logger.LogInformation("Connected to Slack WebSocket");

            var buffer = new byte[128 * 1024];

            while (ws.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(buffer, stoppingToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("WebSocket connection closed by server");
                    break;
                }

                var payload = Encoding.UTF8.GetString(buffer, 0, result.Count);
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;

                // Socket Mode envelopes: type, envelope_id, payload/event
                var envelopeId = root.TryGetProperty("envelope_id", out var eid) ? eid.GetString() : null;

                if (root.TryGetProperty("type", out var t) && t.GetString() == "events_api")
                {
                    // Ack immediately
                    if (envelopeId != null) await AckAsync(ws, envelopeId, stoppingToken);

                    var ev = root.GetProperty("payload").GetProperty("event");
                    var evType = ev.GetProperty("type").GetString();

                    switch (evType)
                    {
                        case "dnd_updated": // DND toggled
                        case "dnd_updated_user": // DND toggled
                            HandleUserDndEvent(ev);
                            break;

                        case "user_change": // profile status changed
                            HandleUserChangeEvent(ev);
                            break;

                        default:
                            _logger.LogInformation("Event Type {evType} not handled", evType);
                            break;
                    }
                }
            }

            // Clean disconnection if possible
            if (ws.State == WebSocketState.Open)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Application shutting down", stoppingToken);
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

        private void HandleUserDndEvent(JsonElement ev)
        {
            try
            {
                var slackEvent = CreateEvent<SlackDndChangedEventArgs>(ev);

                _logger.LogInformation("User DND change -> {statusChangedEvent}", slackEvent);

                UserDnChanged?.Invoke(this, slackEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling user_change event");
            }
        }

        private void HandleUserChangeEvent(JsonElement ev)
        {
            try
            {
                var statusChangedEvent = CreateEvent<SlackUserStatusChangedEventArgs>(ev);

                _logger.LogInformation("User status change -> {statusChangedEvent}", statusChangedEvent);

                UserStatusChanged?.Invoke(this, statusChangedEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling user_change event");
            }
        }

        private T CreateEvent<T>(JsonElement ev) where T : SlackUserEventArgs, new()
        {
            T eventArgs = new T();


            var user = ev.GetProperty("user");


            if (eventArgs is SlackUserStatusChangedEventArgs statusChanged)
            {
                eventArgs.UserId = user.GetProperty("id").GetString();

                var profile = user.GetProperty("profile");

                try
                {
                    statusChanged.Name = profile.GetProperty("real_name").GetString() ?? string.Empty;
                    statusChanged.StatusText = profile.GetProperty("status_text").GetString() ?? string.Empty;
                    statusChanged.StatusEmoji = profile.GetProperty("status_emoji").GetString() ?? string.Empty;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error parsing dnd_updated event");
                }
            }
            else if (eventArgs is SlackDndChangedEventArgs dndChanged)
            {
                dndChanged.UserId = user.GetString();
                var dndStatus = ev.GetProperty("dnd_status");
                dndChanged.IsDoNotDisturbEnabled = dndStatus.GetProperty("dnd_enabled").GetBoolean();

                try
                {
                    var next_dnd_start_ts = dndStatus.GetProperty("next_dnd_start_ts").GetInt64();
                    var next_dnd_end_ts = dndStatus.GetProperty("next_dnd_end_ts").GetInt64();
                    var next_dnd_start = DateTimeOffset.FromUnixTimeSeconds(next_dnd_start_ts).ToLocalTime();
                    var next_dnd_end = DateTimeOffset.FromUnixTimeSeconds(next_dnd_end_ts).ToLocalTime();
                }
                catch(Exception ex)
                {
                    _logger.LogError(ex, "Error parsing dnd_updated event");
                }
            }

            return eventArgs;
        }
    }

}