using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TransportOpenData.TripPlanner.Models;

namespace TransportOpenData.TripPlanner
{
    /// <summary>
    /// Client for accessing the NSW Transport Trip Planner API
    /// </summary>
    public class TripPlannerClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<TripPlannerClient> _logger;
        private readonly TransportOpenDataConfig _config;

        public TripPlannerClient(IOptions<TransportOpenDataConfig> config, ILogger<TripPlannerClient> logger)
        {
            _config = config.Value;
            _logger = logger;
            _httpClient = new HttpClient();

            // Set the base address for the client
            _httpClient.BaseAddress = new Uri(_config.BaseUrl);

            // Set the API key in the Authorization header
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"apikey {_config.ApiKey}");
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        /// <summary>
        /// Gets trip information between the specified locations
        /// </summary>
        /// <param name="originId">The origin location ID</param>
        /// <param name="destId">The destination location ID</param>
        /// <returns>The trip planning response</returns>
        public async Task<TripPlannerResponse?> GetTripAsync(string originId, string destId)
        {
            try
            {
                var requestUrl = $"/trip?outputFormat=rapidJSON&coordOutputFormat=EPSG:4326&depArrMacro=dep" +
                    $"&type_origin=any&name_origin={originId}" +
                    $"&type_destination=any&name_destination={destId}";

                var response = await _httpClient.GetAsync(requestUrl);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<TripPlannerResponse>(content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving trip information");
                return null;
            }
        }

        /// <summary>
        /// Searches for stops by name
        /// </summary>
        /// <param name="searchTerm">The search term to find stops</param>
        /// <returns>The stop finder response</returns>
        public async Task<StopFinderResponse?> FindStopsAsync(string searchTerm)
        {
            try
            {
                //var requestUrl = $"/tp/stop_finder?outputFormat=rapidJSON&type_sf=any&name_sf={searchTerm}";
                var requestUrl = $"tp/stop_finder?outputFormat=rapidJSON&type_sf=stop&name_sf={searchTerm}&coordOutputFormat=EPSG%3A4326&TfNSWSF=true&version=10.2.1.42";

                var response = await _httpClient.GetAsync(requestUrl);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<StopFinderResponse>(content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding stops");
                return null;
            }
        }

        /// <summary>
        /// Gets departure information for a specific stop
        /// </summary>
        /// <param name="stopId">The ID of the stop</param>
        /// <returns>The departure monitor response</returns>
        public async Task<DepartureMonitorResponse?> GetDeparturesAsync(string stopId)
        {
            try
            {
                var requestUrl = $"/departure_mon?outputFormat=rapidJSON&coordOutputFormat=EPSG:4326&mode=direct&type_dm=stop&name_dm={stopId}";

                var response = await _httpClient.GetAsync(requestUrl);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<DepartureMonitorResponse>(content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving departure information");
                return null;
            }
        }
    }
}