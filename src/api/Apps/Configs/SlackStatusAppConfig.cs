namespace AwtrixSharpWeb.Apps.Configs
{
    public class SlackStatusAppConfig : AppConfig 
    {
        public string SlackUserId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the ValueMaps for mapping Slack status to display values.
        /// Each ValueMap should have a ValueMatcher (regex pattern) and can have additional
        /// properties such as Icon, Text, Color, etc.
        /// </summary>
        public List<ValueMap> ValueMaps { get; set; } = new List<ValueMap>();
    }
}
