namespace AwtrixSharpWeb.HostedServices
{
    public class SlackUserStatusChangedEventArgs : SlackUserEventArgs
    {
        public string Name { get; set; } = string.Empty;

        public string StatusText { get; set; } = string.Empty;

        public string StatusEmoji { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{base.ToString()}, Name={Name}: {StatusEmoji} {StatusText}";
        }
    }

}