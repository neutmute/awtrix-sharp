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
            var message = new AwtrixAppMessage2()
                    .SetText(e.StatusText)
                    .SetRainbow(true);

            var result = _awtrixService.Notify(_awtrixAddress, message).Result;
        }
    }
}
