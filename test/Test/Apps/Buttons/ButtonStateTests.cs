using AwtrixSharpWeb.Apps.MqttRender;
using Microsoft.Extensions.Time.Testing;

namespace Test.Apps.Buttons
{
    public class ButtonStateTests
    {
        [Fact]
        public void Constructor_SetsButtonAndTopic()
        {
            var sut = new ButtonState(Button.Left, "topic/left");

            Assert.Equal(Button.Left, sut.Button);
            Assert.Equal("topic/left", sut.Topic);
            Assert.False(sut.IsPressed);
        }

        [Fact]
        public void RegisterChange_TransitionToPressed_FiresClick()
        {
            var sut = new ButtonState(Button.Left, "topic/left");
            ButtonEventArgs received = null;
            sut.Click += (s, e) => received = e;

            sut.RegisterChange(true);

            Assert.NotNull(received);
            Assert.Equal(Button.Left, received.Button);
            Assert.True(sut.IsPressed);
        }

        [Fact]
        public void RegisterChange_StayingPressed_DoesNotFireAgain()
        {
            var sut = new ButtonState(Button.Left, "topic/left");
            var clickCount = 0;
            sut.Click += (s, e) => clickCount++;

            sut.RegisterChange(true);
            sut.RegisterChange(true);

            Assert.Equal(1, clickCount);
        }

        [Fact]
        public void RegisterChange_Release_DoesNotFireClickOrDoubleClick()
        {
            var sut = new ButtonState(Button.Left, "topic/left");
            var clickCount = 0;
            var doubleClickCount = 0;
            sut.Click += (s, e) => clickCount++;
            sut.DoubleClick += (s, e) => doubleClickCount++;

            sut.RegisterChange(false);

            Assert.Equal(0, clickCount);
            Assert.Equal(0, doubleClickCount);
            Assert.False(sut.IsPressed);
        }

        [Fact]
        public void RegisterChange_PressReleasePressQuickly_FiresDoubleClick()
        {
            var sut = new ButtonState(Button.Select, "topic/select");
            var clickCount = 0;
            var doubleClickCount = 0;
            sut.Click += (s, e) => clickCount++;
            sut.DoubleClick += (s, e) => doubleClickCount++;

            sut.RegisterChange(true);  // first press -> Click
            sut.RegisterChange(false); // release
            sut.RegisterChange(true);  // second press, in-process so well within 300ms -> DoubleClick

            Assert.Equal(1, clickCount);
            Assert.Equal(1, doubleClickCount);
        }

        [Fact]
        public void ToString_IncludesButtonAndPressedState()
        {
            var sut = new ButtonState(Button.Right, "topic/right");

            Assert.Equal("Button=Right, IsPressed=False", sut.ToString());
        }

        [Fact]
        public void RegisterChange_PressReleasePressSlowly_FiresTwoClicksAndNoDoubleClick()
        {
            var time = new FakeTimeProvider();
            var sut = new ButtonState(Button.Select, "topic/select", time);
            var clickCount = 0;
            var doubleClickCount = 0;
            sut.Click += (s, e) => clickCount++;
            sut.DoubleClick += (s, e) => doubleClickCount++;

            sut.RegisterChange(true);
            sut.RegisterChange(false);
            time.Advance(TimeSpan.FromMilliseconds(500));
            sut.RegisterChange(true);

            Assert.Equal(2, clickCount);
            Assert.Equal(0, doubleClickCount);
        }
    }
}
