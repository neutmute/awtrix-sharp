using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;

namespace AwtrixSharpWeb.Apps.SlackStatus
{
    /// <summary>
    /// Mirror your slack status to the Awtrix
    /// </summary>
    public class SlackStatusApp : AwtrixApp<SlackStatusAppConfig>
    {
        public const string UserIdConfigKey = "SlackUserId";
        public const string UserIdEnvironmentVariable = "AWTRIXSHARP_SLACK__USERID";

        private const int DefaultDurationSeconds = 50;

        private readonly ISlackConnector _slackConnector;
        private readonly SerialWorkQueue _publishQueue = new();
        private string? _trackingUserId;
        private long _statusVersion;

        public SlackStatusApp(
            ILogger logger
            , SlackStatusAppConfig config
            , AwtrixAddress awtrixAddress
            , IAwtrixService awtrixService
            , ISlackConnector slackConnector
            , SlackSettings? slackSettings = null) : base(logger, config, awtrixAddress, awtrixService)
        {
            _slackConnector = slackConnector;
            _slackSettings = slackSettings;
        }

        /// <summary>
        /// Optional injected Slack settings (CR-14). When supplied, a blank SlackUserId falls back to
        /// <see cref="SlackSettings.UserId"/> (which already carries the environment fallback when bound through DI);
        /// when null (hand-built instances), the literal AWTRIXSHARP_SLACK__USERID variable is read, as before.
        /// </summary>
        private readonly SlackSettings? _slackSettings;

        protected override void Initialize()
        {
            var userId = _slackSettings == null
                ? Config.Config.Get(UserIdConfigKey, UserIdEnvironmentVariable)
                : Program.FirstNonBlank(Config.Config.Get(UserIdConfigKey), _slackSettings.UserId);

            if (string.IsNullOrWhiteSpace(userId))
            {
                Logger.LogWarning(
                    "SlackStatusApp on {BaseTopic}: no Slack user id configured (Config:{ConfigKey} or {EnvironmentVariable}); Slack status will not be shown",
                    AwtrixAddress.BaseTopic, UserIdConfigKey, UserIdEnvironmentVariable);
                return;
            }

            _trackingUserId = userId.Trim();
            _slackConnector.UserStatusChanged += UserStatusChanged;
            Logger.LogInformation("Slack monitoring userId='{SlackUserId}'", _trackingUserId);
        }

        /// <summary>
        /// WS4 (CR-31): detach from the Slack connector on dispose. Keep this override when rewriting this file.
        /// Removing a handler that was never added (no user id) is a no-op.
        /// </summary>
        protected override void ReleaseResources()
        {
            _slackConnector.UserStatusChanged -= UserStatusChanged;
            // Statuses queued but not yet started are skipped after this point. Not a full guarantee: a ShowStatusAsync
            // already running, or a user_change SlackNet was dispatching concurrently with the unsubscribe (whose
            // increment lands after this one), can still publish once after dispose.
            Interlocked.Increment(ref _statusVersion);
            base.ReleaseResources();
        }

        private void UserStatusChanged(object? sender, SlackUserStatusChangedEventArgs e)
        {
            if (e is null || _trackingUserId is null || !string.Equals(_trackingUserId, e.UserId, StringComparison.Ordinal))
            {
                return;
            }

            // Never block or throw on SlackNet's dispatch thread. Publishes are serialised so an older update
            // cannot land after a newer clear; a status superseded while queued is skipped (latest wins).
            var version = Interlocked.Increment(ref _statusVersion);
            _ = FireAndLog(() => _publishQueue.Enqueue(() => IsLatest(version) ? ShowStatusAsync(e) : Task.CompletedTask), nameof(UserStatusChanged));
        }

        private bool IsLatest(long version) => Interlocked.Read(ref _statusVersion) == version;

        private async Task ShowStatusAsync(SlackUserStatusChangedEventArgs e)
        {
            Logger.LogInformation("SlackApp: {SlackStatus}", e);

            if (string.IsNullOrEmpty(e.StatusText))
            {
                Logger.LogInformation("Clearing status");
                await AppClear();
                return;
            }

            var message = new AwtrixAppMessage();

            // Look for a matching value map on the text, then the emoji
            if (!DecorateIfMatch(e.StatusText, message) && !DecorateIfMatch(e.StatusEmoji, message))
            {
                // No mapping found, use default behavior
                message.SetText(e.StatusText);
                message.SetDuration(DefaultDurationSeconds);
            }

            Logger.LogInformation("Slack status message: {Message}", message);
            await AppUpdate(message);
        }

        private bool DecorateIfMatch(string? value, AwtrixAppMessage message)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            var valueMap = Config.FindMatchingValueMap(value);
            if (valueMap == null)
            {
                return false;
            }

            Logger.LogInformation("ValueMap matched for '{Value}'", value);
            valueMap.Decorate(message, Logger);
            return true;
        }
    }
}
