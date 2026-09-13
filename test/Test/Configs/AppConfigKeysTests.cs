using AwtrixSharpWeb.Apps.Configs;

namespace Test.Configs
{
    public class AppConfigKeysTests
    {
        [Fact]
        public void Get_ExistingKey_ReturnsValue()
        {
            var sut = new AppConfigKeys { { "Key", "Value" } };

            Assert.Equal("Value", sut.Get("Key"));
        }

        [Fact]
        public void Get_MissingKey_ReturnsNull()
        {
            var sut = new AppConfigKeys();

            Assert.Null(sut.Get("Missing"));
        }

        [Fact]
        public void Get_WithEnvironmentFallback_UsesConfigValue_WhenPresent()
        {
            var sut = new AppConfigKeys { { "Key", "ConfigValue" } };

            var result = sut.Get("Key", "SOME_ENV_VAR_THAT_SHOULD_NOT_BE_USED");

            Assert.Equal("ConfigValue", result);
        }

        [Fact]
        public void Get_WithEnvironmentFallback_UsesEnvironmentVariable_WhenConfigMissing()
        {
            const string envVarName = "AWTRIXSHARP_TEST_APPCONFIGKEYS_FALLBACK";
            Environment.SetEnvironmentVariable(envVarName, "EnvValue");
            try
            {
                var sut = new AppConfigKeys();

                var result = sut.Get("Missing", envVarName);

                Assert.Equal("EnvValue", result);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVarName, null);
            }
        }

        [Fact]
        public void Get_WithEnvironmentFallback_UsesEnvironmentVariable_WhenConfigValueIsWhitespace()
        {
            const string envVarName = "AWTRIXSHARP_TEST_APPCONFIGKEYS_FALLBACK2";
            Environment.SetEnvironmentVariable(envVarName, "EnvValue");
            try
            {
                var sut = new AppConfigKeys { { "Key", "   " } };

                var result = sut.Get("Key", envVarName);

                Assert.Equal("EnvValue", result);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVarName, null);
            }
        }

        [Fact]
        public void Clone_ProducesIndependentCopy()
        {
            var sut = new AppConfigKeys { { "Key", "Value" } };

            var clone = sut.Clone();
            clone["Key"] = "Changed";
            clone.Add("NewKey", "NewValue");

            Assert.Equal("Value", sut.Get("Key"));
            Assert.Null(sut.Get("NewKey"));
            Assert.Equal("Changed", clone.Get("Key"));
        }

        [Fact]
        public void Clone_OfEmptyDictionary_IsEmpty()
        {
            var sut = new AppConfigKeys();

            var clone = sut.Clone();

            Assert.Empty(clone);
        }

        [Fact]
        public void ToString_WhenEmpty_ReturnsPlaceholder()
        {
            var sut = new AppConfigKeys();

            Assert.Equal("<empty>", sut.ToString());
        }

        [Fact]
        public void ToString_WithEntries_JoinsKeyValuePairs()
        {
            var sut = new AppConfigKeys { { "A", "1" }, { "B", "2" } };

            Assert.Equal("A=1; B=2", sut.ToString());
        }
    }
}
