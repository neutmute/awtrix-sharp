using System.Globalization;
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.MqttRender;

namespace Test.Configs
{
    /// <summary>
    /// Covers AppConfig.GetConfig/SetConfig type conversion (ConvertValue), key lookup and defaults.
    /// </summary>
    public class AppConfigConvertValueTests
    {
        [Fact]
        public void GetConfig_String_ReturnsRawValue()
        {
            var sut = new AppConfig();
            sut.SetConfig("Key", "hello");

            Assert.Equal("hello", sut.GetConfig<string>("Key"));
        }

        [Fact]
        public void GetConfig_MissingKey_ForReferenceType_ReturnsNull()
        {
            var sut = new AppConfig();

            Assert.Null(sut.GetConfig<string>("Missing"));
        }

        [Fact]
        public void GetConfig_Int_ParsesValue()
        {
            var sut = new AppConfig();
            sut.SetConfig("Key", 42);

            Assert.Equal(42, sut.GetConfig<int>("Key"));
        }

        [Fact]
        public void GetConfig_NullableInt_MissingKey_ReturnsNull()
        {
            var sut = new AppConfig();

            Assert.Null(sut.GetConfig<int?>("Missing"));
        }

        [Fact]
        public void GetConfig_Long_ParsesValue()
        {
            var sut = new AppConfig();
            sut.SetConfig("Key", 12345678901L);

            Assert.Equal(12345678901L, sut.GetConfig<long>("Key"));
        }

        [Fact]
        public void GetConfig_Double_ParsesValue()
        {
            var sut = new AppConfig();
            sut.SetConfig("Key", "3.14");

            Assert.Equal(3.14, sut.GetConfig<double>("Key"));
        }

        [Fact]
        public void GetConfig_Decimal_ParsesValue()
        {
            var sut = new AppConfig();
            sut.SetConfig("Key", "3.14");

            Assert.Equal(3.14m, sut.GetConfig<decimal>("Key"));
        }

        [Fact]
        public void GetConfig_Bool_ParsesValue()
        {
            var sut = new AppConfig();
            sut.SetConfig("Key", true);

            Assert.True(sut.GetConfig<bool>("Key"));
        }

        [Fact]
        public void GetConfig_DateTime_ParsesValue()
        {
            var sut = new AppConfig();
            var dateTime = new DateTime(2025, 1, 2, 3, 4, 5);
            sut.SetConfig("Key", dateTime.ToString("O"));

            Assert.Equal(dateTime, sut.GetConfig<DateTime>("Key"));
        }

        [Fact]
        public void GetConfig_TimeSpan_ParsesValue()
        {
            var sut = new AppConfig();
            sut.SetConfig("Key", "00:05:00");

            Assert.Equal(TimeSpan.FromMinutes(5), sut.GetConfig<TimeSpan>("Key"));
        }

        [Fact]
        public void GetConfig_Guid_ParsesValue()
        {
            var sut = new AppConfig();
            var guid = Guid.NewGuid();
            sut.SetConfig("Key", guid.ToString());

            Assert.Equal(guid, sut.GetConfig<Guid>("Key"));
        }

        [Fact]
        public void GetConfig_Enum_ParsesValueCaseInsensitive()
        {
            var sut = new AppConfig();
            sut.SetConfig("Key", "left");

            Assert.Equal(Button.Left, sut.GetConfig<Button>("Key"));
        }

        [Fact]
        public void GetConfig_ListOfValueMap_DeserializesJson()
        {
            var sut = new AppConfig();
            sut.SetConfig("Key", "[{\"ValueMatcher\":\"busy\"}]");

            var result = sut.GetConfig<List<ValueMap>>("Key");

            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("busy", result[0].ValueMatcher);
        }

        [Fact]
        public void GetConfig_ListOfValueMap_InvalidJson_ReturnsEmptyList()
        {
            var sut = new AppConfig();
            sut.SetConfig("Key", "not valid json");

            var result = sut.GetConfig<List<ValueMap>>("Key");

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void GetConfig_UnsupportedType_ThrowsNotSupportedException()
        {
            var sut = new AppConfig();
            sut.SetConfig("Key", "http://example.com");

            Assert.Throws<NotSupportedException>(() => sut.GetConfig<Uri>("Key"));
        }

        [Fact]
        public void GetConfig_MissingKey_NonNullableValueType_ReturnsDefault()
        {
            // CR-23: a missing optional value-type key used to throw NullReferenceException at the (T)null cast.
            var sut = new AppConfig();

            Assert.Equal(TimeSpan.Zero, sut.GetConfig<TimeSpan>("Missing"));
            Assert.Equal(0, sut.GetConfig<int>("Missing"));
        }

        [Fact]
        public void GetConfig_WhitespaceValue_NonStringType_ReturnsDefault()
        {
            var sut = new AppConfig();
            sut.SetConfig("Key", "   ");

            Assert.Equal(TimeSpan.Zero, sut.GetConfig<TimeSpan>("Key"));
            Assert.Equal("   ", sut.GetConfig<string>("Key"));
        }

        [Fact]
        public void GetConfig_Double_UsesInvariantCulture_OnCommaDecimalHost()
        {
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                var sut = new AppConfig();
                sut.SetConfig("Key", "3.14");

                Assert.Equal(3.14, sut.GetConfig<double>("Key"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void SetConfig_Double_WritesInvariantCulture_OnCommaDecimalHost()
        {
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                var sut = new AppConfig();
                sut.SetConfig("Key", 3.5);

                Assert.Equal("3.5", sut.Config.Get("Key"));
                Assert.Equal(3.5, sut.GetConfig<double>("Key"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void SetConfig_NewKey_AddsEntry()
        {
            var sut = new AppConfig();

            sut.SetConfig("Key", "value");

            Assert.Equal("value", sut.Config.Get("Key"));
        }

        [Fact]
        public void SetConfig_ExistingKey_OverwritesEntry()
        {
            var sut = new AppConfig();
            sut.SetConfig("Key", "first");

            sut.SetConfig("Key", "second");

            Assert.Equal("second", sut.Config.Get("Key"));
        }

        [Fact]
        public void SetConfig_NullValue_StoresNull()
        {
            var sut = new AppConfig();

            sut.SetConfig<string>("Key", null);

            Assert.Null(sut.Config.Get("Key"));
        }

        [Fact]
        public void FindMatchingValueMap_ReturnsFirstMatch()
        {
            var sut = new AppConfig();
            sut.ValueMaps.Add(new ValueMap { ValueMatcher = "busy" });
            sut.ValueMaps.Add(new ValueMap { ValueMatcher = "b.*y" });

            var match = sut.FindMatchingValueMap("busy");

            Assert.Same(sut.ValueMaps[0], match);
        }

        [Fact]
        public void FindMatchingValueMap_NoMatch_ReturnsNull()
        {
            var sut = new AppConfig();
            sut.ValueMaps.Add(new ValueMap { ValueMatcher = "busy" });

            var match = sut.FindMatchingValueMap("available");

            Assert.Null(match);
        }

        [Fact]
        public void FindMatchingValueMap_NoValueMaps_ReturnsNull()
        {
            var sut = new AppConfig();

            Assert.Null(sut.FindMatchingValueMap("anything"));
        }

        [Fact]
        public void ValueMaps_SetToNull_BecomesEmptyList()
        {
            var sut = new AppConfig();

            sut.ValueMaps = null;

            Assert.NotNull(sut.ValueMaps);
            Assert.Empty(sut.ValueMaps);
        }

        [Fact]
        public void WithName_SetsType_WhenNonEmpty()
        {
            var sut = new AppConfig();

            var result = sut.WithName("MyApp");

            Assert.Same(sut, result);
            Assert.Equal("MyApp", sut.Type);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void WithName_DoesNotOverwriteType_WhenNameBlank(string name)
        {
            var sut = new AppConfig();
            sut.Type = "Original";

            sut.WithName(name);

            Assert.Equal("Original", sut.Type);
        }

        [Fact]
        public void As_CopiesConfigEnvironmentTypeAndValueMaps()
        {
            var sut = new AppConfig();
            sut.Type = "Original";
            sut.Environment = "dev";
            sut.Config.Add("Key", "Value");
            sut.ValueMaps.Add(new ValueMap { ValueMatcher = "busy" });

            var target = sut.As<ScheduledAppConfig>();

            Assert.Equal("Original", target.Type);
            Assert.Equal("dev", target.Environment);
            Assert.Equal("Value", target.Config.Get("Key"));
            Assert.Single(target.ValueMaps);
        }

        [Fact]
        public void As_ClonesConfigDictionary_SoMutationsAreIndependent()
        {
            var sut = new AppConfig();
            sut.Config.Add("Key", "Value");

            var target = sut.As<ScheduledAppConfig>();
            target.Config["Key"] = "Changed";

            Assert.Equal("Value", sut.Config.Get("Key"));
        }

        [Fact]
        public void Empty_SetsEnvironment()
        {
            var result = AppConfig.Empty("production");

            Assert.Equal("production", result.Environment);
        }

        [Fact]
        public void ToString_IncludesTypeAndConfig()
        {
            var sut = new AppConfig();
            sut.Type = "MyApp";
            sut.Config.Add("Key", "Value");

            var result = sut.ToString();

            Assert.Contains("MyApp", result);
            Assert.Contains("Key=Value", result);
        }
    }
}
