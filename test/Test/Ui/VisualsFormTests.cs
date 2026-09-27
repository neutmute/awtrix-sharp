using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Ui.Models;

namespace Test.Ui
{
    public class VisualsFormTests
    {
        [Fact]
        public void Defaults_BuildMinimalNotification()
        {
            var form = new VisualsForm();

            Assert.Equal("{\"text\":\"Awtrix Sharp!\",\"durationMs\":5000,\"textColor\":\"#FFFFFF\"}", form.Build().ToJson());
            Assert.Empty(form.Validate());
        }

        [Fact]
        public void EveryField_MapsToItsKey()
        {
            var form = new VisualsForm
            {
                Target = VisualsTarget.CustomApp,
                Text = "t", TextColor = "#FF0000", BackgroundColor = "0,0,255", TextCase = TextCase.Upper, Icon = "1234", IconMode = IconMode.Push, Font = "large",
                Effect = "Plasma", EffectSpeed = 2.5, Palette = "Lava", PaletteBlend = false, Overlay = "snow",
                DurationMs = 1000, LifetimeMs = 60000, Hold = true, TextBlinkMs = 300, TextFadeMs = 400,
                ScrollMode = "bounce", ScrollSpeed = 50, TransitionEffect = "Melt", TransitionDirection = "reverse", TransitionDurationMs = 800,
            };

            var message = form.Build();

            Assert.Equal("t", message["text"]);
            Assert.Equal("#FF0000", message["textColor"]);
            Assert.Equal(new[] { 0, 0, 255 }, (int[])message["backgroundColor"]!);
            Assert.Equal(TextCase.Upper, message["textCase"]);
            Assert.Equal("1234", message["icon"]);
            Assert.Equal(IconMode.Push, message["iconMode"]);
            Assert.Equal("large", message["font"]);
            Assert.Equal("Plasma", message["effect"]);
            Assert.Equal(2.5, message["effectSpeed"]);
            Assert.Equal("Lava", message["palette"]);
            Assert.Equal(false, message["paletteBlend"]);
            Assert.Equal("snow", message["overlay"]);
            Assert.Equal(1000, message["durationMs"]);
            Assert.Equal(60000, message["lifetimeMs"]);
            Assert.Equal(true, message["hold"]);
            Assert.Equal(300, message["textBlinkMs"]);
            Assert.Equal(400, message["textFadeMs"]);
            Assert.Contains("\"scroll\":{\"mode\":\"bounce\",\"speed\":50}", message.ToJson());
            Assert.Equal("Melt", message["transitionEffect"]);
            Assert.Equal("reverse", message["transitionDirection"]);
            Assert.Equal(800, message["transitionDurationMs"]);
        }

        [Fact]
        public void BlankAndZeroFields_AreOmitted()
        {
            var form = new VisualsForm { Text = "", TextColor = "", DurationMs = 0, EffectSpeed = 1.0, PaletteBlend = true };

            Assert.Equal("{}", form.Build().ToJson());
        }

        [Fact]
        public void PaletteStops_BuildArray_AndWinOverName()
        {
            var form = new VisualsForm { Palette = "Lava", PaletteStops = "255,0,0;0,0,255" };

            Assert.Contains("\"palette\":[[255,0,0],[0,0,255]]", form.Build().ToJson());
        }

        [Fact]
        public void EffectSpeed_NotOne_IsSent()
        {
            var form = new VisualsForm { EffectSpeed = 1.0 };
            Assert.DoesNotContain("effectSpeed", form.Build().ToJson());

            form.EffectSpeed = 3;
            Assert.Contains("\"effectSpeed\":3", form.Build().ToJson());
        }

        [Fact]
        public void TransitionKeys_OnlyForCustomApp()
        {
            var form = new VisualsForm { Target = VisualsTarget.Notify, TransitionEffect = "Melt" };

            Assert.DoesNotContain("transitionEffect", form.Build().ToJson());
            Assert.Contains(form.Validate(), e => e.Contains("transition", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Validate_ReportsBadColour_BadStops_AndSpeedRange()
        {
            var form = new VisualsForm { TextColor = "red", BackgroundColor = "1,2", PaletteStops = "a;b", EffectSpeed = 11 };

            var errors = form.Validate();

            Assert.Equal(4, errors.Count);
            Assert.DoesNotContain("textColor", form.Build().ToJson());
            Assert.DoesNotContain("palette", form.Build().ToJson());
        }

        [Fact]
        public void Reset_RestoresDefaults()
        {
            var form = new VisualsForm { Effect = "Plasma", Text = "x", Target = VisualsTarget.CustomApp };

            form.Reset();

            Assert.Equal(new VisualsForm().Build().ToJson(), form.Build().ToJson());
            Assert.Equal(VisualsTarget.Notify, form.Target);
        }

        [Fact]
        public void Presets_AllBuild_WithoutErrors()
        {
            Assert.Equal(5, VisualsPresets.All.Count);
            foreach (var preset in VisualsPresets.All)
            {
                var form = new VisualsForm();
                preset.Apply(form);
                Assert.Empty(form.Validate());
                Assert.NotEqual("{}", form.Build().ToJson());
            }
        }
    }
}
