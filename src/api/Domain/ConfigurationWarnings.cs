using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.HostedServices;
using TransportOpenData;

namespace AwtrixSharpWeb.Domain
{
    /// <summary>
    /// Startup checks for missing secrets that would otherwise only surface hours later (CR-14). Never throws.
    /// </summary>
    internal static class ConfigurationWarnings
    {
        public static IReadOnlyList<string> Get(AwtrixConfig awtrix, TransportOpenDataConfig transportOpenData, SlackSettings slack)
        {
            var configuredTypes = (awtrix.Devices ?? Array.Empty<DeviceConfig>())
                .Where(d => d != null)
                .SelectMany(d => d.Apps ?? new List<AppConfig>())
                .Select(a => a?.Type)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToHashSet(StringComparer.Ordinal);

            var warnings = new List<string>();

            if (configuredTypes.Contains(AppNames.TripTimerApp) && string.IsNullOrWhiteSpace(transportOpenData.ApiKey))
            {
                warnings.Add("TripTimerApp is configured but no Transport NSW API key was found " +
                             "(TransportOpenData:ApiKey or TRANSPORTOPENDATA__APIKEY); departure lookups will be rejected");
            }

            if (configuredTypes.Contains(AppNames.SlackStatusApp) && string.IsNullOrWhiteSpace(slack.AppToken))
            {
                warnings.Add("SlackStatusApp is configured but no Slack app token was found " +
                             "(Slack:AppToken or AWTRIXSHARP_SLACK__APPTOKEN); Slack status will not be shown");
            }

            return warnings;
        }
    }
}
