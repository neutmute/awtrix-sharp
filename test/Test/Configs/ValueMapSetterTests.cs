using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.SlackStatus;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Globalization;
using System.Reflection;

namespace Test.Configs
{
    /// <summary>
    /// CR-34: every AwtrixAppMessage setter is reachable from a ValueMap; problems are logged once at load.
    /// </summary>
    public class ValueMapSetterTests
    {
        private readonly Mock<ILogger> _logger = new();

        private static int WarningCount(Mock<ILogger> logger) =>
            logger.Invocations.Count(i => i.Method.Name == nameof(ILogger.Log) && (LogLevel)i.Arguments[0] == LogLevel.Warning);

        // ---------- Setter table ----------

        [Fact]
        public void SetterTable_CoversEveryPublicSingleArgumentSetter()
        {
            var setterNames = typeof(AwtrixAppMessage)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name.StartsWith("Set", StringComparison.Ordinal) && m.GetParameters().Length == 1)
                .Where(m => m.GetParameters()[0].ParameterType != typeof(TimeSpan))
                .Select(m => m.Name.Substring(3))
                .Distinct()
                .ToList();

            Assert.Equal(27, setterNames.Count);
            foreach (var name in setterNames)
            {
                Assert.True(ValueMapSetters.IsKnown(name), $"ValueMap key '{name}' has no setter table entry");
            }
        }

        [Fact]
        public void Decorate_TypedSetters_AreApplied()
        {
            var map = new ValueMap
            {
                { "ValueMatcher", "busy" },
                { "EffectSpeed", "0.5" },
                { "Palette", "255,0,0;0,255,0" },
                { "BarChart", "1,2" },
                { "ProgressColor", "255,0,0" },
                { "IconMode", "pushOnce" },
            };
            var message = new AwtrixAppMessage();

            map.Decorate(message, _logger.Object);

            Assert.Equal(0.5, message["effectSpeed"]);
            Assert.Equal(new[] { new[] { 255, 0, 0 }, new[] { 0, 255, 0 } }, message["palette"]);
            Assert.Equal(new[] { 1, 2 }, message["barChart"]);
            Assert.Equal(new[] { 255, 0, 0 }, message["progressColor"]);
            Assert.Equal(IconMode.PushOnce, message["iconMode"]);
        }

        [Fact]
        public void Decorate_UnderCommaDecimalCulture_ParsesDoublesInvariantly()
        {
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                var message = new AwtrixAppMessage();

                new ValueMap { { "EffectSpeed", "0.5" } }.Decorate(message, _logger.Object);

                Assert.Equal(0.5, message["effectSpeed"]);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void Decorate_KeysAreCaseInsensitive()
        {
            var message = new AwtrixAppMessage();

            new ValueMap { { "ICON", "42" }, { "effectspeed", "7" } }.Decorate(message, _logger.Object);

            Assert.Equal("42", message["icon"]);
            Assert.Equal(7.0, message["effectSpeed"]);
        }

        [Fact]
        public void Decorate_ValueMatcherKeyInAnyCase_IsSkipped()
        {
            var message = new AwtrixAppMessage();

            new ValueMap { { "valueMatcher", "busy" } }.Decorate(message, _logger.Object);

            Assert.Empty(message);
        }

        [Fact]
        public void Decorate_InvalidValue_IsNotAppliedAndDoesNotWarn()
        {
            var message = new AwtrixAppMessage();

            var ex = Record.Exception(() => new ValueMap { { "EffectSpeed", "abc" }, { "BarChart", "1,x" }, { "IconMode", "1" }, { "TextColor", "FF0000" } }.Decorate(message, _logger.Object));

            Assert.Null(ex);
            Assert.False(message.ContainsKey("effectSpeed"));
            Assert.False(message.ContainsKey("barChart"));
            Assert.False(message.ContainsKey("iconMode"));
            Assert.False(message.ContainsKey("textColor"));
            Assert.Equal(0, WarningCount(_logger));
        }

        // ---------- GetConfigurationProblems ----------

        [Fact]
        public void GetConfigurationProblems_ShippedAppSettingsMaps_HaveNoProblems()
        {
            var shipped = new[]
            {
                new ValueMap { { "ValueMatcher", "" }, { "Icon", "1667" }, { "Text", "Go now!" }, { "TextColor", "#FFFFFF" } },
                new ValueMap { { "ValueMatcher", "^-" }, { "Icon", "52465" }, { "TextColor", "#FF0000" } },
                new ValueMap { { "ValueMatcher", "^(?!-).*" }, { "Icon", "52464" }, { "TextColor", "#FFDE21" } },
                new ValueMap { { "ValueMatcher", "busy" }, { "Icon", "38789" }, { "Text", "Busy" }, { "TextColor", "#FF0000" }, { "BackgroundColor", "#FFFFFF" }, { "DurationMs", "60000" } },
            };

            Assert.All(shipped, map => Assert.Empty(map.GetConfigurationProblems()));
        }

        [Fact]
        public void GetConfigurationProblems_UnknownKey_IsReported()
        {
            var problems = new ValueMap { { "ValueMatcher", "busy" }, { "Colour", "#FF0000" } }.GetConfigurationProblems();

            Assert.Contains(problems, p => p.Contains("Colour"));
        }

        [Fact]
        public void GetConfigurationProblems_InvalidValue_IsReported()
        {
            var problems = new ValueMap { { "ValueMatcher", "busy" }, { "DurationMs", "a minute" } }.GetConfigurationProblems();

            Assert.Contains(problems, p => p.Contains("DurationMs") && p.Contains("a minute"));
        }

        [Theory]
        [InlineData("EffectSpeed", "NaN")]
        [InlineData("EffectSpeed", "Infinity")]
        [InlineData("EffectSpeed", "-Infinity")]
        [InlineData("EffectSpeed", "1e999")]
        public void NonFiniteNumber_IsReportedOnce_AndNotApplied(string key, string value)
        {
            var map = new ValueMap { { "ValueMatcher", "busy" }, { key, value } };
            var message = new AwtrixAppMessage();

            Assert.Contains(map.GetConfigurationProblems(), p => p.Contains(key) && p.Contains(value));
            map.Decorate(message, _logger.Object);
            Assert.Empty(message);
        }

        [Fact]
        public void GetConfigurationProblems_InvalidRegex_IsReported_AndIsMatchStillFallsBackToSubstring()
        {
            var map = new ValueMap { { "ValueMatcher", "[this is not a valid regex" } };

            Assert.Contains(map.GetConfigurationProblems(), p => p.Contains("regular expression"));
            Assert.True(map.IsMatch("meeting with [this is not a valid regex"));
            Assert.False(map.IsMatch("available"));
        }

        // ---------- Logged once at load ----------

        [Fact]
        public void LogValueMapProblems_LogsOneWarningPerProblem()
        {
            var config = new AppConfig { Type = "MqttRenderApp" };
            config.ValueMaps = new List<ValueMap>
            {
                new() { { "ValueMatcher", "ok" }, { "Colour", "#FF0000" } },
                new() { { "ValueMatcher", "[bad" }, { "DurationMs", "soon" } },
            };

            config.LogValueMapProblems(_logger.Object, "awtrix/clock1");

            Assert.Equal(3, WarningCount(_logger));
        }

        [Fact]
        public void AppConstruction_WithBadValueMap_WarnsOnce_AndDecorateDoesNotWarnAgain()
        {
            var config = new SlackStatusAppConfig { Type = "SlackStatusApp" };
            config.ValueMaps = new List<ValueMap> { new() { { "ValueMatcher", "busy" }, { "Colour", "#FF0000" } } };

            _ = new SlackStatusApp(_logger.Object, config, new AwtrixAddress { BaseTopic = "awtrix/clock1" },
                new Mock<IAwtrixService>().Object, new Mock<ISlackConnector>().Object);

            Assert.Equal(1, WarningCount(_logger));

            for (var i = 0; i < 3; i++)
            {
                config.ValueMaps[0].Decorate(new AwtrixAppMessage(), _logger.Object);
            }

            Assert.Equal(1, WarningCount(_logger));
        }

        [Fact]
        public void GetConfigurationProblems_BareHexColour_IsReportedAsInvalid()
        {
            // Review Focus 1: an AWTRIX 3 style "FF0000" must never be sent (NG rejects the whole payload)
            var map = new ValueMap { { "ValueMatcher", "x" }, { "TextColor", "FF0000" } };
            var problem = Assert.Single(map.GetConfigurationProblems());
            Assert.Contains("TextColor", problem);
            Assert.Contains("FF0000", problem);
            var message = new AwtrixAppMessage();
            map.Decorate(message, _logger.Object);
            Assert.False(message.ContainsKey("textColor"));
        }

        [Fact]
        public void GetConfigurationProblems_UnknownKey_MentionsMigrationDoc()
        {
            var map = new ValueMap { { "ValueMatcher", "x" }, { "Color", "#FF0000" } };
            Assert.Contains("docs/config-migration.md", Assert.Single(map.GetConfigurationProblems()));
        }

        [Theory]
        [InlineData("Rainbow")]
        [InlineData(" Ocean ")]
        public void Decorate_PaletteName_IsStoredTrimmed(string value)
        {
            var message = new AwtrixAppMessage();
            new ValueMap { { "Palette", value } }.Decorate(message, _logger.Object);
            Assert.Equal(value.Trim(), message["palette"]);
        }

        [Theory]
        [InlineData("255,0,0;0,x")]
        [InlineData("1,2;3,4")]
        [InlineData("300,0,0")]
        [InlineData("255,0,0;")]
        public void Decorate_MalformedPaletteMatrix_IsRejected(string value)
        {
            Assert.False(ValueMapSetters.IsValidValue("Palette", value));

            var message = new AwtrixAppMessage();
            new ValueMap { { "Palette", value } }.Decorate(message, _logger.Object);
            Assert.False(message.ContainsKey("palette"));
        }
    }
}
