using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using MQTTnet;
using System.Text;

namespace AwtrixSharpWeb.Apps.MqttRender
{
    public class ButtonApp: AwtrixApp<AppConfig>
    {
        IMqttConnector _mqttConnector;

        Dictionary<Button, ButtonState> _buttonTopics;


        public event EventHandler<ButtonEventArgs>? Click;

        public event EventHandler<ButtonEventArgs>? DoubleClick;

        public ButtonApp(
            ILogger logger
            , AppConfig config
            , AwtrixAddress awtrixAddress
            , IAwtrixService awtrixService
            , IMqttConnector mqttConnector)
            : base(logger, config, awtrixAddress, awtrixService)
        {

            _mqttConnector = mqttConnector;

            _buttonTopics = new Dictionary<Button, ButtonState>
            {
                 { Button.Left, BuildState(Button.Left)}
                ,{ Button.Select, BuildState(Button.Select)}
                ,{ Button.Right, BuildState(Button.Right)}
            };
        }

        private ButtonState BuildState(Button button)
        {
            var buttonState = new ButtonState(button, GetTopic(button));
            return buttonState;
        }

        private string GetTopic(Button button)
        {
            return $"{AwtrixAddress.BaseTopic}/stats/button{button.ToString()}";
        }

        protected override void Initialize()
        {
            foreach (var buttonState in _buttonTopics.Values)
            {
                // Subscribe records the topic and never throws (MqttConnector); don't block startup waiting for SUBACK
                var topic = buttonState.Topic;
                _ = FireAndLog(() => _mqttConnector.Subscribe(topic), $"Subscribe {topic}");
                buttonState.Click += (s, e) => Click?.Invoke(this, e);
                buttonState.DoubleClick += (s, e) => DoubleClick?.Invoke(this, e);
            }
            _mqttConnector.MessageReceived += RawMessageReceived;
        }

        /// <summary>
        /// WS4 (CR-31): detach from the connector on dispose. Keep this override when rewriting this file.
        /// </summary>
        protected override void ReleaseResources()
        {
            _mqttConnector.MessageReceived -= RawMessageReceived;
            base.ReleaseResources();
        }

        private async Task RawMessageReceived(MqttApplicationMessageReceivedEventArgs args)
        {
            foreach (var keyValuePair in _buttonTopics)
            {
                var state = keyValuePair.Value;
                if (args.ApplicationMessage.Topic == state.Topic)
                {
                    bool isPressed = Encoding.UTF8.GetString(args.ApplicationMessage.Payload) == "1";
                    state.RegisterChange(isPressed);
                }
            }
        }
    }
}
