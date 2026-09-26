using AwtrixSharpWeb.Domain;

namespace Test.Domain
{
    public class AwtrixAppMessageTest
    {
        [Fact]
        public void ToJsonTextSimple() => Assert.Equal("{\"text\":\"Hello World\"}", new AwtrixAppMessage().SetText("Hello World").ToJson());

        [Fact]
        public void SetText_StringStartingWithBracket_IsPlainText()
        {
            // No more "text that looks like JSON is parsed": fragments are built with TextFragment
            Assert.Equal("{\"text\":\"[not json\"}", new AwtrixAppMessage().SetText("[not json").ToJson());
        }

        [Fact]
        public void TextFragment_InvalidColour_Throws() => Assert.Throws<ArgumentException>(() => new TextFragment("a", "FF0000"));
    }
}
