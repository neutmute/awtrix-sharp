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
                .Select(m => m.Name.Substring(3))
                .Distinct()
                .ToList();

            Assert.Equal(30, setterNames.Count);
            foreach (var name in setterNames)
            {
                Assert.True(ValueMapSetters.IsKnown(name), $"ValueMap key '{name}' has no setter table entry");
            }
        }

        [Fact]
        public void Decorate_DoubleAndArraySetters_AreApplied()
        {
            var map = new ValueMap
            {
                { "ValueMatcher", "busy" },
                { "BlinkText", "0.5" },
                { "FadeText", "1.5" },
                { "Gradient", "255,0,0;0,255,0" },
                { "Bar", "1,2" },
                { "ProgressC", "255,0,0" },
            };
            var message = new AwtrixAppMessage();

            map.Decorate(message, _logger.Object);

            Assert.Equal("0.5", message["blinkText"]);
            Assert.Equal("1.5", message["fadeText"]);
            Assert.Equal("255,0,0;0,255,0", message["gradient"]);
            Assert.Equal("1,2", message["bar"]);
            Assert.Equal("255,0,0", message["progressC"]);
            Assert.Equal("{\"blinkText\":\"0.5\",\"fadeText\":\"1.5\",\"gradient\":[[255,0,0],[0,255,0]],\"bar\":[1,2],\"progressC\":[255,0,0]}", message.ToJson());
        }

        [Fact]
        public void Decorate_UnderCommaDecimalCulture_ParsesDoublesInvariantly()
        {
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                var message = new AwtrixAppMessage();

                new ValueMap { { "BlinkText", "0.5" } }.Decorate(message, _logger.Object);

                Assert.Equal("0.5", message["blinkText"]);
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
            Assert.Equal("7", message["effectSpeed"]);
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

            var ex = Record.Exception(() => new ValueMap { { "BlinkText", "fast" }, { "Bar", "1,x" } }.Decorate(message, _logger.Object));

            Assert.Null(ex);
            Assert.Empty(message);
            Assert.Equal(0, WarningCount(_logger));
        }

        // ---------- GetConfigurationProblems ----------

        [Fact]
        public void GetConfigurationProblems_ShippedAppSettingsMaps_HaveNoProblems()
        {
            var shipped = new[]
            {
                new ValueMap { { "ValueMatcher", "" }, { "Icon", "1667" }, { "Text", "Go now!" }, { "Color", "#FFFFFF" } },
                new ValueMap { { "ValueMatcher", "^-" }, { "Icon", "52465" }, { "Color", "#FF0000" } },
                new ValueMap { { "ValueMatcher", "^(?!-).*" }, { "Icon", "52464" }, { "Color", "#FFDE21" } },
                new ValueMap { { "ValueMatcher", "busy" }, { "Icon", "38789" }, { "Text", "Busy" }, { "Color", "#FF0000" }, { "Background", "#FFFFFF" }, { "Duration", "60" } },
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
            var problems = new ValueMap { { "ValueMatcher", "busy" }, { "Duration", "a minute" } }.GetConfigurationProblems();

            Assert.Contains(problems, p => p.Contains("Duration") && p.Contains("a minute"));
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
                new() { { "ValueMatcher", "[bad" }, { "Duration", "soon" } },
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
    }
}
