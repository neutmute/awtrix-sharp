using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using Microsoft.Extensions.Logging;
using Moq;

namespace Test.Configs
{
    /// <summary>
    /// Additional ValueMap coverage: dynamic property application via reflection, ordering,
    /// no-match handling and cloning. (test/Test/Configs/ValueMapsTests.cs is left untouched.)
    /// </summary>
    public class ValueMapDecorateTests
    {
        private readonly Mock<ILogger> _mockLogger = new Mock<ILogger>();

        [Fact]
        public void IsMatch_NoMatchingText_ReturnsFalse()
        {
            var valueMap = new ValueMap { { "ValueMatcher", "busy" } };

            Assert.False(valueMap.IsMatch("available"));
        }

        [Fact]
        public void IsMatch_NullInput_ReturnsFalse()
        {
            var valueMap = new ValueMap { { "ValueMatcher", "busy" } };

            Assert.False(valueMap.IsMatch(null));
        }

        [Fact]
        public void Decorate_AppliesTextIconColorDurationCenter()
        {
            var valueMap = new ValueMap
            {
                { "ValueMatcher", "busy" },
                { "Text", "I am busy" },
                { "Icon", "12345" },
                { "TextColor", "255,0,0" },
                { "DurationMs", "30000" },
                { "TextCenter", "true" }
            };

            var message = new AwtrixAppMessage();
            valueMap.Decorate(message, _mockLogger.Object);

            Assert.Equal("I am busy", message.Text);
            Assert.Equal("12345", message["icon"]);
            Assert.Equal(new[] { 255, 0, 0 }, message["textColor"]);
            Assert.Equal(30000, message["durationMs"]);
            Assert.Equal(true, message["textCenter"]);
        }

        [Fact]
        public void Decorate_InvalidDuration_IsIgnoredWithoutThrowing()
        {
            var valueMap = new ValueMap
            {
                { "ValueMatcher", "busy" },
                { "DurationMs", "not-a-number" }
            };

            var message = new AwtrixAppMessage();
            var ex = Record.Exception(() => valueMap.Decorate(message, _mockLogger.Object));

            Assert.Null(ex);
            Assert.False(message.ContainsKey("durationMs"));
        }

        [Fact]
        public void Decorate_InvalidCenter_IsIgnoredWithoutThrowing()
        {
            var valueMap = new ValueMap
            {
                { "ValueMatcher", "busy" },
                { "TextCenter", "not-a-bool" }
            };

            var message = new AwtrixAppMessage();
            valueMap.Decorate(message, _mockLogger.Object);

            Assert.False(message.ContainsKey("textCenter"));
        }

        [Fact]
        public void Decorate_DynamicProperty_AppliesViaReflectionForIntSetter()
        {
            var valueMap = new ValueMap
            {
                { "ValueMatcher", "busy" },
                { "Progress", "50" }
            };

            var message = new AwtrixAppMessage();
            valueMap.Decorate(message, _mockLogger.Object);

            Assert.Equal(50, message["progress"]);
        }

        [Fact]
        public void Decorate_DynamicProperty_AppliesViaReflectionForBoolSetter()
        {
            var valueMap = new ValueMap
            {
                { "ValueMatcher", "busy" },
                { "Hold", "true" }
            };

            var message = new AwtrixAppMessage();
            valueMap.Decorate(message, _mockLogger.Object);

            Assert.Equal(true, message["hold"]);
        }

        [Fact]
        public void Decorate_DynamicProperty_AppliesViaReflectionForStringSetter()
        {
            var valueMap = new ValueMap
            {
                { "ValueMatcher", "busy" },
                { "Overlay", "clouds" }
            };

            var message = new AwtrixAppMessage();
            valueMap.Decorate(message, _mockLogger.Object);

            Assert.Equal("clouds", message["overlay"]);
        }

        [Fact]
        public void Decorate_DynamicProperty_AppliesViaReflectionForIntArraySetter()
        {
            var valueMap = new ValueMap
            {
                { "ValueMatcher", "busy" },
                { "LineChart", "1,2,3" }
            };

            var message = new AwtrixAppMessage();
            valueMap.Decorate(message, _mockLogger.Object);

            Assert.Equal(new[] { 1, 2, 3 }, message["lineChart"]);
        }

        [Fact]
        public void Decorate_UnknownPropertyName_IsIgnoredWithoutThrowing()
        {
            var valueMap = new ValueMap
            {
                { "ValueMatcher", "busy" },
                { "ThisPropertyDoesNotExist", "value" }
            };

            var message = new AwtrixAppMessage();
            var ex = Record.Exception(() => valueMap.Decorate(message, _mockLogger.Object));

            Assert.Null(ex);
        }

        [Fact]
        public void Decorate_MultipleProperties_AllApplied_RegardlessOfOrder()
        {
            var valueMap = new ValueMap
            {
                { "ValueMatcher", "busy" },
                { "Icon", "1" },
                { "Text", "hi" },
                { "EffectSpeed", "10" },
                { "TextColor", "1,2,3" }
            };

            var message = new AwtrixAppMessage();
            valueMap.Decorate(message, _mockLogger.Object);

            Assert.Equal("hi", message.Text);
            Assert.Equal("1", message["icon"]);
            Assert.Equal(new[] { 1, 2, 3 }, message["textColor"]);
            Assert.Equal(10.0, message["effectSpeed"]);
        }

        [Fact]
        public void Decorate_SkipsValueMatcherKeyItself()
        {
            var valueMap = new ValueMap { { "ValueMatcher", "busy" } };

            var message = new AwtrixAppMessage();
            valueMap.Decorate(message, _mockLogger.Object);

            Assert.Empty(message);
        }

        [Fact]
        public void Clone_ProducesIndependentCopy()
        {
            var valueMap = new ValueMap { { "ValueMatcher", "busy" }, { "Icon", "1" } };

            var clone = valueMap.Clone();
            clone["Icon"] = "2";

            Assert.Equal("1", valueMap["Icon"]);
            Assert.Equal("2", clone["Icon"]);
            Assert.Equal("busy", clone.ValueMatcher);
        }

        [Fact]
        public void ToString_ReturnsValueMatcher()
        {
            var valueMap = new ValueMap { { "ValueMatcher", "busy" } };

            Assert.Equal("ValueMatcher=busy", valueMap.ToString());
        }
    }
}
