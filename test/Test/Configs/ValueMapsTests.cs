//using AwtrixSharpWeb.Apps.Configs;
//using AwtrixSharpWeb.Domain;
//using Microsoft.Extensions.Logging;
//using Moq;
//using System.Text.Json;
//using Xunit;
//using Xunit.Abstractions;

//namespace Test.Configs
//{
//    public class ValueMapsTests
//    {
//        private readonly Mock<ILogger> _mockLogger;
//        private readonly JsonSerializerOptions _jsonOptions;
//        private readonly ITestOutputHelper _output;

//        public ValueMapsTests(ITestOutputHelper output)
//        {
//            _output = output;
//            _mockLogger = new Mock<ILogger>();
//            _jsonOptions = new JsonSerializerOptions
//            {
//                PropertyNameCaseInsensitive = true,
//                AllowTrailingCommas = true
//            };
//            _jsonOptions.Converters.Add(new ValueMapJsonConverter());
//        }

//        [Fact]
//        public void IsMatch_WithValidRegex_ShouldMatchCorrectly()
//        {
//            // Arrange
//            var valueMap = new ValueMap
//            {
//                { "ValueMatcher", "busy|in.*meeting" }
//            };

//            // Act & Assert
//            Assert.True(valueMap.IsMatch("busy"));
//            Assert.True(valueMap.IsMatch("in a meeting"));
//            Assert.True(valueMap.IsMatch("in important meeting"));
//            Assert.False(valueMap.IsMatch("available"));
//            Assert.False(valueMap.IsMatch("free"));
//        }

//        [Fact]
//        public void IsMatch_WithInvalidRegex_ShouldFallbackToContains()
//        {
//            // Arrange
//            var valueMap = new ValueMap
//            {
//                { "ValueMatcher", "[this is not a valid regex" }
//            };

//            // Act & Assert
//            Assert.True(valueMap.IsMatch("meeting with [this is not a valid regex"));
//            Assert.False(valueMap.IsMatch("available"));
//        }

//        [Fact]
//        public void IsMatch_WithEmptyMatcher_ShouldReturnFalse()
//        {
//            // Arrange
//            var valueMap = new ValueMap();
//            var valueMapWithEmpty = new ValueMap { { "ValueMatcher", "" } };

//            // Act & Assert
//            Assert.False(valueMap.IsMatch("any text"));
//            Assert.False(valueMapWithEmpty.IsMatch("any text"));
//        }

//        [Fact]
//        public void IsMatch_CaseInsensitive_ShouldMatchCorrectly()
//        {
//            // Arrange
//            var valueMap = new ValueMap
//            {
//                { "ValueMatcher", "busy" }
//            };

//            // Act & Assert
//            Assert.True(valueMap.IsMatch("Busy"));
//            Assert.True(valueMap.IsMatch("BUSY"));
//            Assert.True(valueMap.IsMatch("I am BUSY today"));
//        }

//        [Fact]
//        public void Decorate_ShouldApplyBasicProperties()
//        {
//            // Arrange
//            var valueMap = new ValueMap
//            {
//                { "ValueMatcher", "busy" },
//                { "Icon", "12345" },
//                { "Text", "I am busy" },
//                { "Color", "255,0,0" }
//            };

//            var message = new AwtrixAppMessage();

//            // Act
//            valueMap.Decorate(message, _mockLogger.Object);

//            // Assert
//            Assert.Equal("I am busy", message.Text);
//            Assert.Equal("12345", message["icon"]);
//            Assert.Equal("255,0,0", message["color"]);
//        }

//        [Fact]
//        public void Decorate_ShouldApplyDurationProperty()
//        {
//            // Arrange
//            var valueMap = new ValueMap
//            {
//                { "ValueMatcher", "busy" },
//                { "Duration", "30" }
//            };

//            var message = new AwtrixAppMessage();

//            // Act
//            valueMap.Decorate(message, _mockLogger.Object);

//            // Assert
//            Assert.Equal("30", message["duration"]);
//        }

//        [Fact]
//        public void Decorate_ShouldApplyDynamicProperties()
//        {
//            // Arrange
//            var valueMap = new ValueMap
//            {
//                { "ValueMatcher", "busy" },
//                { "Center", "true" },
//                { "EffectSpeed", "50" },
//                { "ScrollSpeed", "100" }
//            };

//            var message = new AwtrixAppMessage();

//            // Act
//            valueMap.Decorate(message, _mockLogger.Object);

//            // Assert
//            Assert.Equal("true", message["center"]);
//            Assert.Equal("50", message["effectSpeed"]);
//            Assert.Equal("100", message["scrollSpeed"]);
//        }

//        [Fact]
//        public void Deserialize_ShouldConvertJsonToValueMaps()
//        {
//            // Arrange
//            string json = @"[
//                {
//                    ""valueMatcher"": ""busy"",
//                    ""icon"": ""12345"",
//                    ""color"": ""255,0,0"",
//                    ""text"": ""I am busy""
//                },
//                {
//                    ""valueMatcher"": ""meeting"",
//                    ""icon"": ""54321"",
//                    ""color"": ""0,0,255"",
//                    ""duration"": ""60""
//                }
//            ]";

//            // Act
//            var valueMaps = JsonSerializer.Deserialize<List<ValueMap>>(json, _jsonOptions);

//            // Assert
//            Assert.NotNull(valueMaps);
//            Assert.Equal(2, valueMaps.Count);
            
//            Assert.Equal("busy", valueMaps[0].ValueMatcher);
//            Assert.Equal("12345", valueMaps[0]["icon"]);
//            Assert.Equal("255,0,0", valueMaps[0]["color"]);
//            Assert.Equal("I am busy", valueMaps[0]["text"]);

//            Assert.Equal("meeting", valueMaps[1].ValueMatcher);
//            Assert.Equal("54321", valueMaps[1]["icon"]);
//            Assert.Equal("0,0,255", valueMaps[1]["color"]);
//            Assert.Equal("60", valueMaps[1]["duration"]);
//        }

//        [Fact]
//        public void DebugIsMatch_CheckIfValueMatcherWorksAsExpected()
//        {
//            // Arrange
//            var valueMap = new ValueMap { { "ValueMatcher", "busy" } };
//            var input = "I am busy with meetings";
            
//            // Act & Assert
//            Assert.True(valueMap.IsMatch(input), $"Expected '{valueMap.ValueMatcher}' to match '{input}'");
//        }

//        [Fact]
//        public void DebugDirectRegex_CheckIfRegexMatchWorks()
//        {
//            // Direct regex test
//            var regex = new System.Text.RegularExpressions.Regex("busy", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
//            var input = "I am busy with meetings";
            
//            Assert.True(regex.IsMatch(input), $"Regex '{regex}' should match '{input}'");
//        }

//        [Fact]
//        public void SimplifiedEndToEndTest()
//        {
//            // Create a simple ValueMap manually
//            var valueMap = new ValueMap 
//            { 
//                { "ValueMatcher", "busy" },
//                { "Text", "Busy Status" },
//                { "Icon", "12345" }
//            };
            
//            // Verify the IsMatch method works
//            var input = "I am busy with meetings";
//            Assert.True(valueMap.IsMatch(input), $"ValueMap with matcher '{valueMap.ValueMatcher}' should match '{input}'");
            
//            // Apply to a message
//            var message = new AwtrixAppMessage();
//            valueMap.Decorate(message, _mockLogger.Object);
            
//            // Verify decoration worked
//            Assert.Equal("Busy Status", message.Text);
//            Assert.Equal("12345", message["icon"]);
//        }

//        [Fact]
//        public void Deserialize_AndApply_CompleteEndToEndTest()
//        {
//            // Arrange
//            string json = @"[{""valueMatcher"":""busy"",""icon"":""12345"",""color"":""255,0,0"",""text"":""Busy Status"",""center"":""true"",""duration"":""45""}]";

//            // Output the JSON for debugging
//            _output.WriteLine($"JSON: {json}");

//            // Act - Deserialize
//            var valueMaps = JsonSerializer.Deserialize<List<ValueMap>>(json, _jsonOptions);
//            Assert.NotNull(valueMaps);
//            Assert.Single(valueMaps);
            
//            // Verify and output first ValueMap
//            var firstMap = valueMaps[0];
//            _output.WriteLine($"First map: ValueMatcher={firstMap.ValueMatcher}, Text={firstMap["text"]}, Keys={string.Join(", ", firstMap.Keys)}");
//            Assert.Equal("busy", firstMap.ValueMatcher);
            
//            // Create and output input string
//            var busyInput = "I am busy with meetings";
//            _output.WriteLine($"Input string: {busyInput}");
            
//            // Test the match directly
//            var isMatch = firstMap.IsMatch(busyInput);
//            _output.WriteLine($"Direct match result: {isMatch}");
//            Assert.True(isMatch, $"Expected '{firstMap.ValueMatcher}' to match '{busyInput}'");
            
//            // Try to find manually using FirstOrDefault
//            var matchingMap = valueMaps.FirstOrDefault(m => 
//            {
//                var result = m.IsMatch(busyInput);
//                _output.WriteLine($"Checking map with ValueMatcher={m.ValueMatcher}, IsMatch={result}");
//                return result;
//            });
            
//            // Verify we found a match
//            Assert.NotNull(matchingMap);
//            _output.WriteLine($"Found matching map: {matchingMap.ValueMatcher}");
            
//            // Create message
//            var message = new AwtrixAppMessage();
            
//            // Apply the map
//            matchingMap.Decorate(message, _mockLogger.Object);
            
//            // Output message properties
//            _output.WriteLine($"Message after decoration: Text={message.Text}, Keys={string.Join(", ", message.Keys)}");
            
//            // Verify the decoration worked
//            Assert.NotNull(message.Text);
//            Assert.Equal("Busy Status", message.Text);
//            Assert.Equal("12345", message["icon"]);
//            Assert.Equal("255,0,0", message["color"]);
//            Assert.Equal("true", message["center"]);
//            Assert.Equal("45", message["duration"]);
//        }
//    }
//}
