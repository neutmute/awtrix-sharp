using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Services;

namespace AwtrixSharpWeb.Apps
{


    public class SlackApp
    {
        AwtrixAddress _awtrixAddress;
        SlackConnector _slackConnector;
        AwtrixService _awtrixService;
        public SlackApp(AwtrixAddress awtrixAddress, SlackConnector slackConnector, AwtrixService awtrixService)
        {
            _awtrixAddress = awtrixAddress;
            _slackConnector = slackConnector;
            _awtrixService = awtrixService;
        }

        public void Initialize()
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
                    result = _awtrixService.Dismiss(_awtrixAddress);
                }
                else
                {
                    var message = new AwtrixAppMessage2()
                            .SetText(e.StatusText)
                            .SetHold()
                            .SetRainbow();

                    result = _awtrixService.Notify(_awtrixAddress, message);
                }
            }
        }
    }
}
