using AwtrixSharpWeb.Apps.Configs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Test.Configs
{
    public class AppConfigTests
    {

        [Fact]
        public void SerialisationComplex() {

            var sut = new AppConfig();

            sut.Type = "TestApp";   
            sut.Config.Add("TestKey", "TestValue");
            sut.Config.Add("TestKey2", "TestValue2");

            sut.ValueMaps.Add(new ValueMap() { ValueMatcher = "myregex" });

            sut.ValueMaps[0].Add("prop1", "value1");

            // Create JSON options with pretty printing enabled
            var options = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            };
            
            // Serialize with the pretty print options
            var json = System.Text.Json.JsonSerializer.Serialize(sut, options);
            
            // Print the JSON to the console for inspection
            Console.WriteLine(json);
        }

        [Fact]
        public void SerialisationConfigs()
        {

            var sut = new AppConfig();

            sut.Type = "TestApp";
            sut.Config.Add("6:00", "Brightness=8");
            sut.Config.Add("7:00", "GlobalTextColor=#FFFFFF");

            // Create JSON options with pretty printing enabled
            var options = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            };

            // Serialize with the pretty print options
            var json = System.Text.Json.JsonSerializer.Serialize(sut, options);

            // Print the JSON to the console for inspection
            Console.WriteLine(json);
        }

        [Fact]
        public void Deserialisations()
        {
            var json = "{\r\n  \"Config\": {\r\n    \"6:00\": \"Brightness=8\",\r\n    \"7:00\": \"GlobalTextColor=#FFFFFF\"\r\n  },\r\n  \"Environment\": null,\r\n  \"Type\": \"TestApp\",\r\n  \"Name\": \"TestApp\",\r\n  \"ValueMaps\": []\r\n}";

            // Serialize with the pretty print options
            var appConfig = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json);

            // Print the JSON to the console for inspection
            Assert.True(appConfig.Config.Count > 0);
        }
    }
}
