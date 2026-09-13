namespace AwtrixSharpWeb.Domain
{
    /// <summary>
    /// The "Slack" configuration section. AWTRIXSHARP_SLACK__APPTOKEN / AWTRIXSHARP_SLACK__USERID arrive here
    /// through the AWTRIXSHARP_ provider; user secrets and appsettings work too. The literal environment
    /// variables stay as a final fallback (CR-14).
    /// </summary>
    public class SlackSettings
    {
        public const string SectionName = "Slack";
        public const string AppTokenEnvironmentVariable = "AWTRIXSHARP_SLACK__APPTOKEN";
        public const string UserIdEnvironmentVariable = "AWTRIXSHARP_SLACK__USERID";

        /// <summary>Slack app-level token (xapp-...)</summary>
        public string? AppToken { get; set; }

        /// <summary>Default Slack user id for SlackStatusApp when the app config leaves SlackUserId blank</summary>
        public string? UserId { get; set; }

        public SlackSettings WithEnvironmentFallback()
        {
            if (string.IsNullOrWhiteSpace(AppToken))
            {
                AppToken = Environment.GetEnvironmentVariable(AppTokenEnvironmentVariable);
            }

            if (string.IsNullOrWhiteSpace(UserId))
            {
                UserId = Environment.GetEnvironmentVariable(UserIdEnvironmentVariable);
            }

            return this;
        }
    }
}
