using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;

namespace AwtrixSharpWeb.Apps
{

    public class SlackStatusApp : AwtrixApp<AppConfig>
    {
        SlackConnector _slackConnector;

        public SlackStatusApp(ILogger logger, AppConfig config, AwtrixAddress awtrixAddress, AwtrixService awtrixService, SlackConnector slackConnector) : base(logger, config, awtrixAddress, awtrixService)
        {
            _slackConnector = slackConnector;
        }

        protected override void Initialize()
        {
            _slackConnector.UserStatusChanged += UserStatusChanged;
        }

        private void UserStatusChanged(object? sender, SlackUserStatusChangedEventArgs e)
        {
            var userId = Environment.GetEnvironmentVariable("AWTRIXSHARP_SLACK__USERID"); // U*** (your user ID)
            if (userId.Equals(e.UserId))
            {
                bool result;
                if (e.StatusText == string.Empty)
                {
                    result = AppClear().Result;
                }
                else
                {
                    var message = new AwtrixAppMessage()
                            .SetText(e.StatusText)
                            .SetHold()
                            .SetRainbow();

                    result = AppUpdate(message).Result;
                }
            }
        }
    }
}
