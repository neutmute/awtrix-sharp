namespace AwtrixSharpWeb.Apps.MqttRender
{
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
}
