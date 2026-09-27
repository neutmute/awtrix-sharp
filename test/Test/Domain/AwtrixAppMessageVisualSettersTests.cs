using AwtrixSharpWeb.Domain;

namespace Test.Domain
{
    public class AwtrixAppMessageVisualSettersTests
    {
        [Fact]
        public void SetFont_SetsAndClears()
        {
            var message = new AwtrixAppMessage().SetFont("large");
            Assert.Equal("{\"font\":\"large\"}", message.ToJson());

            message.SetFont(null);
            Assert.Equal("{}", message.ToJson());
        }

        [Fact]
        public void SetScroll_OnlyNonNullParts()
        {
            Assert.Equal("{\"scroll\":{\"mode\":\"bounce\",\"speed\":60}}", new AwtrixAppMessage().SetScroll("bounce", 60).ToJson());
            Assert.Equal("{\"scroll\":{\"mode\":\"wrap\"}}", new AwtrixAppMessage().SetScroll("wrap", null).ToJson());
            Assert.Equal("{\"scroll\":{\"speed\":60}}", new AwtrixAppMessage().SetScroll(null, 60).ToJson());
            Assert.Equal("{}", new AwtrixAppMessage().SetScroll("wrap", 1).SetScroll(null, null).ToJson());
        }

        [Fact]
        public void SetScrollSpeed_StillWorks()
        {
            Assert.Equal("{\"scroll\":{\"speed\":80}}", new AwtrixAppMessage().SetScrollSpeed(80).ToJson());
        }

        [Fact]
        public void TransitionSetters_UseNgKeys()
        {
            var message = new AwtrixAppMessage()
                .SetTransitionEffect("Melt")
                .SetTransitionDirection("reverse")
                .SetTransitionDurationMs(1500);

            Assert.Equal("{\"transitionDirection\":\"reverse\",\"transitionDurationMs\":1500,\"transitionEffect\":\"Melt\"}", message.ToJson());
        }
    }
}
