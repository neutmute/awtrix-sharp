using AwtrixSharpWeb.Domain;

namespace Test.Domain
{
    public class DeviceCapabilitiesTests
    {
        private static string Fixture() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "ng-capabilities.json"));

        [Fact]
        public void TryParse_RealPayload_ReadsEveryList_AndIgnoresExtraKeys()
        {
            Assert.True(DeviceCapabilities.TryParse(Fixture(), out var caps));

            Assert.Equal(CapabilitiesSource.Device, caps.Source);
            Assert.Equal(19, caps.Effects.Length);
            Assert.Contains("Plasma", caps.Effects);
            Assert.Equal(15, caps.PaletteEffects.Length);
            Assert.Equal(22, caps.Transitions.Length);
            Assert.Equal(new[] { "drizzle", "frost", "rain", "snow", "storm", "thunder" }, caps.Overlays);
            Assert.Equal(8, caps.Palettes.Length);
        }

        [Fact]
        public void TryParse_MissingArray_FallsBackToBuiltIn()
        {
            Assert.True(DeviceCapabilities.TryParse("{\"effects\":[\"OnlyOne\"]}", out var caps));

            Assert.Equal(new[] { "OnlyOne" }, caps.Effects);
            Assert.Equal(NgVisuals.Transitions, caps.Transitions);
            Assert.Equal(NgVisuals.Palettes, caps.Palettes);
            Assert.Equal(CapabilitiesSource.Device, caps.Source);
        }

        [Fact]
        public void TryParse_EmptyArray_FallsBackToBuiltIn()
        {
            Assert.True(DeviceCapabilities.TryParse("{\"effects\":[]}", out var caps));

            Assert.Equal(NgVisuals.Effects, caps.Effects);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("[1,2]")]
        [InlineData("{\"effects\":\"Plasma\"}")]
        public void TryParse_Malformed_ReturnsFalse_AndBuiltIn(string? json)
        {
            Assert.False(DeviceCapabilities.TryParse(json, out var caps));

            Assert.Same(DeviceCapabilities.BuiltIn, caps);
        }

        [Fact]
        public void BuiltIn_MatchesNgVisuals()
        {
            var caps = DeviceCapabilities.BuiltIn;

            Assert.Equal(CapabilitiesSource.BuiltIn, caps.Source);
            Assert.Equal(NgVisuals.Effects, caps.Effects);
            Assert.Equal(NgVisuals.PaletteEffects, caps.PaletteEffects);
            Assert.Equal(NgVisuals.Transitions, caps.Transitions);
            Assert.Equal(NgVisuals.Overlays, caps.Overlays);
            Assert.Equal(NgVisuals.Palettes, caps.Palettes);
        }

        [Fact]
        public void NgVisuals_Counts_MatchReference()
        {
            Assert.Equal(19, NgVisuals.Effects.Length);
            Assert.Equal(22, NgVisuals.Transitions.Length);
            Assert.Equal(6, NgVisuals.Overlays.Length);
            Assert.Equal(8, NgVisuals.Palettes.Length);
            Assert.Equal(new[] { "normal", "reverse" }, NgVisuals.TransitionDirections);
            Assert.Equal(new[] { "small", "large" }, NgVisuals.Fonts);
            Assert.Equal(new[] { "static", "wrap", "loop", "bounce" }, NgVisuals.ScrollModes);
        }
    }
}
