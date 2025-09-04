namespace AwtrixSharpWeb.HostedServices
{
    public class SlackDndChangedEventArgs : SlackUserEventArgs
    {
        public bool IsDoNotDisturbEnabled { get; set; }

        public override string ToString()
        {
            return $"{base.ToString()}: IsDoNotDisturb={IsDoNotDisturbEnabled}";
        }
    }

}