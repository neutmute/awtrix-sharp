using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;

namespace AwtrixSharpWeb.Apps
{

    public class SlackStatusApp : AwtrixApp<SlackStatusAppConfig>
    {
        SlackConnector _slackConnector;
        string _trackingUserId;

        public SlackStatusApp(ILogger logger, SlackStatusAppConfig config, AwtrixAddress awtrixAddress, AwtrixService awtrixService, SlackConnector slackConnector) : base(logger, config, awtrixAddress, awtrixService)
        {
            _slackConnector = slackConnector;
        }

        protected override void Initialize()
        {
            _slackConnector.UserStatusChanged += UserStatusChanged;
            _trackingUserId = Config.Get("SlackUserId", "AWTRIXSHARP_SLACK__USERID");
            Logger.LogInformation("Slack monitoring userId='{_trackingUserId}'", _trackingUserId);
        }

        private void UserStatusChanged(object? sender, SlackUserStatusChangedEventArgs e)
        {
            if (_trackingUserId.Equals(e.UserId))
            {
                bool result;
                if (e.StatusText == string.Empty)
                {
                    Logger.LogInformation("Clearing status");
                    result = AppClear().Result;
                }
                else
                {
                    var message = new AwtrixAppMessage()
                            .SetText(e.StatusText)
                            .SetDuration(50);

                    Logger.LogInformation(message.ToString());

                    result = AppUpdate(message).Result;
                }
            }
        }
    }
}
