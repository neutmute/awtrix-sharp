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
    
        [Fact]
        public void TryApplyJson_MapsKnownKeys_IntoFields()
        {
            var form = new VisualsForm { Target = VisualsTarget.CustomApp };
            var json = "{\"text\":\"hi\",\"textColor\":[255,0,0],\"backgroundColor\":\"#0000FF\",\"textCase\":\"upper\",\"icon\":\"1234\",\"iconMode\":\"push\",\"font\":\"large\","
                     + "\"effect\":\"Plasma\",\"effectSpeed\":2.5,\"palette\":[[255,0,0],[0,0,255]],\"paletteBlend\":false,\"overlay\":\"snow\","
                     + "\"durationMs\":1000,\"lifetimeMs\":60000,\"hold\":true,\"textBlinkMs\":300,\"textFadeMs\":400,\"scroll\":{\"mode\":\"bounce\",\"speed\":50},"
                     + "\"transitionEffect\":\"Melt\",\"transitionDirection\":\"reverse\",\"transitionDurationMs\":800}";

            Assert.True(form.TryApplyJson(json, out var error));

            Assert.Null(error);
            Assert.Equal("hi", form.Text);
            Assert.Equal("255,0,0", form.TextColor);
            Assert.Equal("#0000FF", form.BackgroundColor);
            Assert.Equal(TextCase.Upper, form.TextCase);
            Assert.Equal("1234", form.Icon);
            Assert.Equal(IconMode.Push, form.IconMode);
            Assert.Equal("large", form.Font);
            Assert.Equal("Plasma", form.Effect);
            Assert.Equal(2.5, form.EffectSpeed);
            Assert.Equal("255,0,0;0,0,255", form.PaletteStops);
            Assert.Equal("", form.Palette);
            Assert.False(form.PaletteBlend);
            Assert.Equal("snow", form.Overlay);
            Assert.Equal(1000, form.DurationMs);
            Assert.Equal(60000, form.LifetimeMs);
            Assert.True(form.Hold);
            Assert.Equal(300, form.TextBlinkMs);
            Assert.Equal(400, form.TextFadeMs);
            Assert.Equal("bounce", form.ScrollMode);
            Assert.Equal(50, form.ScrollSpeed);
            Assert.Equal("Melt", form.TransitionEffect);
            Assert.Equal("reverse", form.TransitionDirection);
            Assert.Equal(800, form.TransitionDurationMs);
            Assert.Empty(form.Extras);
            Assert.Equal(VisualsTarget.CustomApp, form.Target);
        }

        [Fact]
        public void TryApplyJson_RoundTrips_ThroughBuild()
        {
            var form = new VisualsForm();
            // Keys in ToJson order (text first, then ordinal) so the string comparison is exact.
            var json = "{\"text\":\"hi\",\"durationMs\":7000,\"effect\":\"Plasma\",\"palette\":\"Lava\",\"scroll\":{\"mode\":\"wrap\"},\"textColor\":\"#00FF00\"}";

            Assert.True(form.TryApplyJson(json, out _));

            Assert.Equal(json, form.Build().ToJson());
        }

        [Fact]
        public void TryApplyJson_UnknownKeys_AreKeptAsExtras_AndBuilt()
        {
            var form = new VisualsForm();

            Assert.True(form.TryApplyJson("{\"text\":\"x\",\"foo\":1,\"draw\":[[\"pixel\",1,2,\"#FF0000\"]]}", out _));
            form.Effect = "Snake";

            var json = form.Build().ToJson();
            Assert.Equal(new[] { "draw", "foo" }, form.Extras.Keys.OrderBy(k => k));
            Assert.Contains("\"foo\":1", json);
            Assert.Contains("\"draw\":[[\"pixel\",1,2,\"#FF0000\"]]", json);
            Assert.Contains("\"effect\":\"Snake\"", json);
        }

        [Fact]
        public void TryApplyJson_ClearsFieldsNotInJson_ButKeepsTargetAndAppName()
        {
            var form = new VisualsForm { Target = VisualsTarget.CustomApp, AppName = "demo", Effect = "Plasma", Hold = true };

            Assert.True(form.TryApplyJson("{\"text\":\"only\"}", out _));

            Assert.Equal("", form.Effect);
            Assert.False(form.Hold);
            Assert.Equal(0, form.DurationMs);
            Assert.Equal("", form.TextColor);
            Assert.Equal("only", form.Text);
            Assert.Equal(VisualsTarget.CustomApp, form.Target);
            Assert.Equal("demo", form.AppName);
        }

        [Fact]
        public void TryApplyJson_WrongShapeForKnownKey_IsKeptAsExtra()
        {
            var form = new VisualsForm();

            Assert.True(form.TryApplyJson("{\"durationMs\":\"soon\",\"scroll\":\"bounce\"}", out _));

            Assert.Equal(0, form.DurationMs);
            Assert.Equal("bounce", form.ScrollMode);
            Assert.True(form.Extras.ContainsKey("durationMs"));
            Assert.Contains("\"durationMs\":\"soon\"", form.Build().ToJson());
        }

        [Fact]
        public void TryApplyJson_BlankText_IsKept()
        {
            var form = new VisualsForm();

            Assert.True(form.TryApplyJson("{\"text\":\"\"}", out _));

            Assert.Equal("", form.Text);
            Assert.Equal("{}", form.Build().ToJson());
        }

        [Fact]
        public void TryApplyJson_InvalidJson_ReportsError_AndLeavesFormAlone()
        {
            var form = new VisualsForm { Effect = "Plasma" };

            Assert.False(form.TryApplyJson("{\"text\":", out var error));

            Assert.Contains("JSON", error!, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Plasma", form.Effect);
        }

        [Fact]
        public void Reset_ClearsExtras()
        {
            var form = new VisualsForm();
            form.TryApplyJson("{\"foo\":1}", out _);

            form.Reset();

            Assert.Empty(form.Extras);
        }
    }
}
