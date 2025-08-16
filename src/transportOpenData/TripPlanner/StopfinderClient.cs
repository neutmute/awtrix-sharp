//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Net.Http;
//using System.Text;
//using System.Threading.Tasks;
//using Microsoft.Extensions.Options;

//namespace TransportOpenData.TripPlanner
//{
//    public partial class StopfinderClient
//    {
//        private readonly TransportOpenDataConfig _config;

//        /// <summary>
//        /// Initializes a new instance of the StopfinderClient class
//        /// The HttpClient is configured in Program.cs to include the X-TEST header
//        /// </summary>
//        /// <param name="httpClient">The pre-configured HTTP client</param>
//        /// <param name="config">The transport open data configuration</param>
//        public StopfinderClient(HttpClient httpClient, IOptions<TransportOpenDataConfig> config)
//        {
//            _config = config.Value;
            
//            // Set the base URL from config
//            BaseUrl = $"{_config.BaseUrl}";
            
//            // Set the HTTP client (already configured with headers in Program.cs)
//            _httpClient = httpClient;
            
//            // Call the partial method for initialization
//            Initialize();
//        }

//        partial void Initialize()
//        {
//            // This is called by the constructor to initialize the client
//            // Additional initialization logic can go here
//        }
//    }
//}
