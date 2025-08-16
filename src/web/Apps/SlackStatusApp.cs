using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;

namespace AwtrixSharpWeb.Apps
{
    public abstract class AwtrixApp
    {
        protected AwtrixAddress AwtrixAddress;
        protected AwtrixService AwtrixService;

        public AwtrixApp(AwtrixAddress awtrixAddress, AwtrixService awtrixService)
        {
            AwtrixAddress = awtrixAddress;
            AwtrixService = awtrixService;
        }

        public abstract void Initialize();
    }

    public class SlackStatusApp : AwtrixApp
    {
        SlackConnector _slackConnector;

        public SlackStatusApp(AwtrixAddress awtrixAddress, AwtrixService awtrixService, SlackConnector slackConnector) : base(awtrixAddress, awtrixService)
        {
            _slackConnector = slackConnector;
        }

        public override void Initialize()
        {
            _slackConnector.UserStatusChanged += UserStatusChanged;
        }

        private void UserStatusChanged(object? sender, SlackUserStatusChangedEventArgs e)
        {
            var userId = Environment.GetEnvironmentVariable("AWTRIXSHARP_SLACK__USERID"); // U*** (your user ID)
            if (userId.Equals(e.UserId))
            {
                Task<bool> result;
                if (e.StatusText == string.Empty)
                {
                    result = AwtrixService.Dismiss(AwtrixAddress);
                }
                else
                {
                    var message = new AwtrixAppMessage()
                            .SetText(e.StatusText)
                            .SetHold()
                            .SetRainbow();

                    result = AwtrixService.Notify(AwtrixAddress, message);
                }
            }
        }
    }
}
