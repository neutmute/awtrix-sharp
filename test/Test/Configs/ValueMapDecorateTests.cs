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
                { "Color", "255,0,0" },
                { "Duration", "30" },
                { "Center", "true" }
            };

            var message = new AwtrixAppMessage();
            valueMap.Decorate(message, _mockLogger.Object);

            Assert.Equal("I am busy", message.Text);
            Assert.Equal("12345", message["icon"]);
            Assert.Equal("255,0,0", message["color"]);
            Assert.Equal("30", message["duration"]);
            Assert.Equal("true", message["center"]);
        }

        [Fact]
        public void Decorate_InvalidDuration_IsIgnoredWithoutThrowing()
        {
            var valueMap = new ValueMap
            {
                { "ValueMatcher", "busy" },
                { "Duration", "not-a-number" }
            };

            var message = new AwtrixAppMessage();
            var ex = Record.Exception(() => valueMap.Decorate(message, _mockLogger.Object));

            Assert.Null(ex);
            Assert.False(message.ContainsKey("duration"));
        }

        [Fact]
        public void Decorate_InvalidCenter_IsIgnoredWithoutThrowing()
        {
            var valueMap = new ValueMap
            {
                { "ValueMatcher", "busy" },
                { "Center", "not-a-bool" }
            };

            var message = new AwtrixAppMessage();
            valueMap.Decorate(message, _mockLogger.Object);

            Assert.False(message.ContainsKey("center"));
        }

        [Fact]
        public void Decorate_DynamicProperty_AppliesViaReflectionForIntSetter()
        {
            var valueMap = new ValueMap
            {
                { "ValueMatcher", "busy" },
                { "EffectSpeed", "50" }
            };

            var message = new AwtrixAppMessage();
            valueMap.Decorate(message, _mockLogger.Object);

            Assert.Equal("50", message["effectSpeed"]);
        }

        [Fact]
        public void Decorate_DynamicProperty_AppliesViaReflectionForBoolSetter()
        {
            var valueMap = new ValueMap
            {
                { "ValueMatcher", "busy" },
                { "Rainbow", "true" }
            };

            var message = new AwtrixAppMessage();
            valueMap.Decorate(message, _mockLogger.Object);

            Assert.Equal("true", message["rainbow"]);
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
                { "Line", "1,2,3" }
            };

            var message = new AwtrixAppMessage();
            valueMap.Decorate(message, _mockLogger.Object);

            Assert.Equal("1,2,3", message["line"]);
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
                { "Color", "1,2,3" }
            };

            var message = new AwtrixAppMessage();
            valueMap.Decorate(message, _mockLogger.Object);

            Assert.Equal("hi", message.Text);
            Assert.Equal("1", message["icon"]);
            Assert.Equal("1,2,3", message["color"]);
            Assert.Equal("10", message["effectSpeed"]);
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
