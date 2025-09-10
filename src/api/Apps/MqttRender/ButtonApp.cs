using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using MQTTnet;
using System.Text;

namespace AwtrixSharpWeb.Apps.MqttRender
{
    public enum Button
    {
        Unknown = 0
        , Left
        , Right
        , Select
    }

    public class ButtonEventArgs : EventArgs
    {
        public Button Button { get; set; } = Button.Unknown;

        public override string ToString()
        {
            return $"Button={Button}";
        }
    }


    public class DoubleClickDetector
    {
        private readonly TimeSpan threshold;
        private DateTime lastClick;

        public DoubleClickDetector(double thresholdMilliseconds = 300)
        {
            threshold = TimeSpan.FromMilliseconds(thresholdMilliseconds);
            lastClick = DateTime.MinValue;
        }

        /// <returns>true if double</returns>
        public bool RegisterClick()
        {
            var now = DateTime.Now;
            if (now - lastClick <= threshold)
            {
                lastClick = DateTime.MinValue;
                return true; // double click detected
            }

            lastClick = now;
            return false;
        }
    }

    public class ButtonState
    {
        DoubleClickDetector _doubleClickDetector { get; set; } = new DoubleClickDetector();

        public Button Button { get; private set; } = Button.Unknown;

        public bool IsPressed { get; private set; } = false;

        public string Topic { get; private set; } = string.Empty;


        public event EventHandler<ButtonEventArgs>? Click;

        public event EventHandler<ButtonEventArgs>? DoubleClick;

        public ButtonState(Button button, string topic)
        {
            Button = button;
            Topic = topic;
        }

        public void RegisterChange(bool newIsPressed)
        {
            if (!IsPressed && newIsPressed)
            {
                var isDoubleClick = _doubleClickDetector.RegisterClick();
                if (isDoubleClick)
                {
                    DoubleClick?.Invoke(this, new ButtonEventArgs { Button = Button });
                }
                else
                {
                    Click?.Invoke(this, new ButtonEventArgs { Button = Button });
                }
            }
            IsPressed = newIsPressed;
        }

        public override string ToString()
        {
            return $"Button={Button}, IsPressed={IsPressed}";
        }
    }
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
            return new ButtonState(button, GetTopic(button));
        }

        private string GetTopic(Button button)
        {
            return $"{AwtrixAddress.BaseTopic}/stats/button{button.ToString()}";
        }

        protected override void Initialize()
        {
            foreach (var buttonState in _buttonTopics.Values)
            {
                _mqttConnector.Subscribe(buttonState.Topic).Wait();
                buttonState.Click += (s, e) => Click?.Invoke(this, e);
                buttonState.DoubleClick += (s, e) => DoubleClick?.Invoke(this, e);
            }
            _mqttConnector.MessageReceived += RawMessageReceived;   
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
