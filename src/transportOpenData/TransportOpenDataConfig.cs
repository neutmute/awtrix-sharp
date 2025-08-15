using System;

namespace TransportOpenData
{
    public class TransportOpenDataConfig
    {
        /// <summary>
        /// The base URL for the NSW Transport Trip Planner API
        /// </summary>
        public string BaseUrl { get; set; } = "https://api.transport.nsw.gov.au/v1";

        /// <summary>
        /// The authorization key used to access the API
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;
    }
}