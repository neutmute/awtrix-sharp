namespace AwtrixSharpWeb.HostedServices
{
    public class SlackUserEventArgs : EventArgs
    {
        public string UserId { get; set; } = string.Empty;


        public override string ToString()
        {
            return $"UserId={UserId}";
        }
    }

}