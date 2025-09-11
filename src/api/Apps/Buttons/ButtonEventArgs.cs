namespace AwtrixSharpWeb.Apps.MqttRender
{
    public class ButtonEventArgs : EventArgs
    {
        public Button Button { get; set; } = Button.Unknown;

        public override string ToString()
        {
            return $"Button={Button}";
        }
    }
}
