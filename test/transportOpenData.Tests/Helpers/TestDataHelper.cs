using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using TransportOpenData.TripPlanner;

namespace TransportOpenData.Tests.Helpers
{
    public static class TestDataHelper
    {
        private static readonly JsonSerializerOptions JsonOptions;

        static TestDataHelper()
        {
            JsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        /// <summary>
        /// Loads a JSON file from the TestData directory and deserializes it to the specified type
        /// </summary>
        public static async Task<T> LoadTestDataAsync<T>(string fileName)
        {
            string filePath = Path.Combine("TestData", fileName);
            string jsonContent = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<T>(jsonContent, JsonOptions);
        }

        /// <summary>
        /// Loads a JSON file from the TestData directory and returns it as a string
        /// </summary>
        public static async Task<string> LoadTestDataRawAsync(string fileName)
        {
            string filePath = Path.Combine("TestData", fileName);
            return await File.ReadAllTextAsync(filePath);
        }

        /// <summary>
        /// Takes a TripRequestResponse and serializes it to JSON for comparison
        /// </summary>
        public static string SerializeToJson<T>(T obj)
        {
            return JsonSerializer.Serialize(obj, JsonOptions);
        }
    }
}