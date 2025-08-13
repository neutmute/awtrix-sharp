
// File: SocketModeEventsWatcher.cs
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
    // Event args for user status changes
    public class SlackUserStatusChangedEventArgs : EventArgs
    {
        public string UserId { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public string StatusEmoji { get; set; } = string.Empty;
    }

    public class SlackConnector : IHostedService  
    {
        ILogger<SlackConnector> _logger;

        public SlackConnector(ILogger<SlackConnector> logger)
        {
            _logger = logger;
        }

        private static readonly HttpClient http = new HttpClient();

        public event EventHandler<SlackUserStatusChangedEventArgs>? UserStatusChanged;

        static async Task<string> OpenSocketUrlAsync(string appToken)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "https://slack.com/api/apps.connections.open");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appToken);
            var res = await http.SendAsync(req);
            var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            if (!json.GetProperty("ok").GetBoolean())
                throw new Exception($"apps.connections.open failed: {json.GetProperty("error").GetString()}");
            return json.GetProperty("url").GetString()!;
        }


        static async Task AckAsync(ClientWebSocket ws, string envelopeId)
        {
            var ack = JsonSerializer.Serialize(new { envelope_id = envelopeId });
            await ws.SendAsync(Encoding.UTF8.GetBytes(ack), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var appToken = Environment.GetEnvironmentVariable("AWTRIXSHARP_SLACK__APPTOKEN"); // xapp-***
            var botToken = Environment.GetEnvironmentVariable("AWTRIXSHARP_SLACK__BOTTOKEN"); // xoxb-*** (if you also want to poll presence)
            var userId = Environment.GetEnvironmentVariable("AWTRIXSHARP_SLACK__USERID");  // U******

            var wsUrl = await OpenSocketUrlAsync(appToken!);

            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

            var buffer = new byte[128 * 1024];

            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var payload = Encoding.UTF8.GetString(buffer, 0, result.Count);
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;

                // Socket Mode envelopes: type, envelope_id, payload/event
                var envelopeId = root.TryGetProperty("envelope_id", out var eid) ? eid.GetString() : null;

                if (root.TryGetProperty("type", out var t) && t.GetString() == "events_api")
                {
                    // Ack immediately
                    if (envelopeId != null) await AckAsync(ws, envelopeId);

                    var ev = root.GetProperty("payload").GetProperty("event");
                    var evType = ev.GetProperty("type").GetString();

                    switch (evType)
                    {
                        case "dnd_updated_user": // DND toggled
                            var u = ev.GetProperty("user").GetString();
                            var dnd = ev.GetProperty("dnd_status").GetProperty("dnd_enabled").GetBoolean();
                            Console.WriteLine($"DND -> {u}: {(dnd ? "ON" : "OFF")}");
                            break;

                        case "user_change": // profile status changed

                            await HandleUserChangeEvent(ev);
                            //var user = ev.GetProperty("user");
                            //var id = user.GetProperty("id").GetString();
                            //var profile = user.GetProperty("profile");
                            //try
                            //{
                            //    var statusText = profile.GetProperty("status_text").GetString();
                            //    var statusEmoji = profile.GetProperty("status_emoji").GetString();
                            //    Console.WriteLine($"STATUS -> {id}: {statusEmoji} {statusText}");
                            //}
                            //catch (Exception e)
                            //{
                            //    Console.WriteLine($"Exception -> {user} , {profile}");
                            //}
                            break;
                    }

                    // (Optional) also poll presence here if you need active/away in near-real-time
                    // using users.getPresence + users.profile.get + dnd.info with botToken.
                }
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
        }

        private Task HandleUserChangeEvent(JsonElement ev)
        {
            try
            {
                var user = ev.GetProperty("user");
                var id = user.GetProperty("id").GetString();
                var profile = user.GetProperty("profile");

                // Extract relevant user data
                string statusText = string.Empty;
                string statusEmoji = string.Empty;

                if (profile.TryGetProperty("status_text", out var statusTextElement))
                    statusText = statusTextElement.GetString() ?? string.Empty;

                if (profile.TryGetProperty("status_emoji", out var statusEmojiElement))
                    statusEmoji = statusEmojiElement.GetString() ?? string.Empty;

                _logger.LogInformation("User status change -> {UserId}: {Emoji} {StatusText}",
                    id, statusEmoji, statusText);

                // Fire the event for subscribers to handle
                UserStatusChanged?.Invoke(this, new SlackUserStatusChangedEventArgs
                {
                    UserId = id ?? string.Empty,
                    StatusText = statusText,
                    StatusEmoji = statusEmoji
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling user_change event");
            }

            return Task.CompletedTask;
        }
    }


}
