using System.Text.Json;
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using Microsoft.Extensions.Logging;
using Moq;

namespace Test.Configs
{
    /// <summary>
    /// Restored ValueMap tests (CR-39): matching semantics, deserialisation of the appsettings shape, first-match-wins
    /// selection, and match-then-decorate end to end. Complements ValueMapDecorateTests (per-setter coverage).
    /// Keys use PascalCase as in appsettings.json; case-insensitive keys are WS7 (CR-22).
    /// </summary>
    public class ValueMapsTests
    {
        private readonly Mock<ILogger> _mockLogger = new();

        [Fact]
        public void IsMatch_WithValidRegex_MatchesAlternatives()
        {
            var valueMap = new ValueMap { { "ValueMatcher", "busy|in.*meeting" } };

            Assert.True(valueMap.IsMatch("busy"));
            Assert.True(valueMap.IsMatch("in a meeting"));
            Assert.True(valueMap.IsMatch("in important meeting"));
            Assert.False(valueMap.IsMatch("available"));
            Assert.False(valueMap.IsMatch("free"));
        }

        [Fact]
        public void IsMatch_WithInvalidRegex_FallsBackToContains()
        {
            var valueMap = new ValueMap { { "ValueMatcher", "[this is not a valid regex" } };

            Assert.True(valueMap.IsMatch("meeting with [this is not a valid regex"));
            Assert.False(valueMap.IsMatch("available"));
        }

        [Fact]
        public void IsMatch_WithMissingOrEmptyMatcher_ReturnsFalse()
        {
            Assert.False(new ValueMap().IsMatch("any text"));
            Assert.False(new ValueMap { { "ValueMatcher", "" } }.IsMatch("any text"));
        }

        [Fact]
        public void IsMatch_IsCaseInsensitive()
        {
            var valueMap = new ValueMap { { "ValueMatcher", "busy" } };

            Assert.True(valueMap.IsMatch("Busy"));
            Assert.True(valueMap.IsMatch("BUSY"));
            Assert.True(valueMap.IsMatch("I am BUSY today"));
        }

        [Theory]
        [InlineData("-120", true, false)]
        [InlineData("350", false, true)]
        [InlineData("0", false, true)]
        public void IsMatch_ShippedMqttRenderMatchers_SplitNegativeAndNonNegative(string reading, bool negative, bool nonNegative)
        {
            // The two ValueMaps shipped for MqttRenderApp in appsettings.json
            Assert.Equal(negative, new ValueMap { { "ValueMatcher", "^-" } }.IsMatch(reading));
            Assert.Equal(nonNegative, new ValueMap { { "ValueMatcher", "^(?!-).*" } }.IsMatch(reading));
        }

        [Fact]
        public void Decorate_AppliesSpeedAndCenterProperties()
        {
            var valueMap = new ValueMap
            {
                { "ValueMatcher", "busy" },
                { "Center", "true" },
                { "EffectSpeed", "50" },
                { "ScrollSpeed", "100" }
            };
            var message = new AwtrixAppMessage();

            valueMap.Decorate(message, _mockLogger.Object);

            Assert.Equal("true", message["center"]);
            Assert.Equal("50", message["effectSpeed"]);
            Assert.Equal("100", message["scrollSpeed"]);
            Assert.False(message.ContainsKey("ValueMatcher"));
        }

        [Fact]
        public void Deserialize_AppsettingsShape_ProducesValueMaps()
        {
            const string json = @"[
                { ""ValueMatcher"": ""busy"", ""Icon"": ""12345"", ""Color"": ""255,0,0"", ""Text"": ""I am busy"" },
                { ""ValueMatcher"": ""meeting"", ""Icon"": ""54321"", ""Color"": ""0,0,255"", ""Duration"": ""60"" }
            ]";

            var valueMaps = JsonSerializer.Deserialize<List<ValueMap>>(json);

            Assert.NotNull(valueMaps);
            Assert.Equal(2, valueMaps!.Count);
            Assert.Equal("busy", valueMaps[0].ValueMatcher);
            Assert.Equal("12345", valueMaps[0]["Icon"]);
            Assert.Equal("I am busy", valueMaps[0]["Text"]);
            Assert.Equal("meeting", valueMaps[1].ValueMatcher);
            Assert.Equal("60", valueMaps[1]["Duration"]);
        }

        [Fact]
        public void FindMatchingValueMap_FirstMatchWins()
        {
            var config = new AppConfig
            {
                ValueMaps = new List<ValueMap>
                {
                    new() { { "ValueMatcher", "busy" }, { "Icon", "first" } },
                    new() { { "ValueMatcher", "bus" }, { "Icon", "second" } }
                }
            };

            Assert.Equal("first", config.FindMatchingValueMap("busy today")!["Icon"]);
            Assert.Equal("second", config.FindMatchingValueMap("bus stop")!["Icon"]);
            Assert.Null(config.FindMatchingValueMap("available"));
        }

        [Fact]
        public void DeserializeMatchAndDecorate_EndToEnd()
        {
            const string json = @"[{""ValueMatcher"":""busy"",""Icon"":""12345"",""Color"":""255,0,0"",""Text"":""Busy Status"",""Center"":""true"",""Duration"":""45""}]";
            var config = new AppConfig { ValueMaps = JsonSerializer.Deserialize<List<ValueMap>>(json)! };
            var message = new AwtrixAppMessage();

            var match = config.FindMatchingValueMap("I am busy with meetings");
            Assert.NotNull(match);
            match!.Decorate(message, _mockLogger.Object);

            Assert.Equal("Busy Status", message.Text);
            Assert.Equal("12345", message["icon"]);
            Assert.Equal("255,0,0", message["color"]);
            Assert.Equal("true", message["center"]);
            Assert.Equal("45", message["duration"]);
        }
    }
}
