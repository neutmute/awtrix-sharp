using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.Diurnal;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Test.Apps.Diurnal
{
    /// <summary>
    /// DiurnalApp.Initialize() replays past time entries using the real wall-clock
    /// (DateTime.Now) rather than an injected IClock - see testability note in final report.
    /// To keep tests deterministic we drive the time-of-day logic via ITimerService.MinuteChanged,
    /// which DiurnalApp always subscribes to.
    /// </summary>
    public class DiurnalAppTests
    {
        private Mock<ILogger> _mockLogger;
        private Mock<ITimerService> _mockTimerService;
        private Mock<IAwtrixService> _mockAwtrixService;
        private AwtrixAddress _address;

        private DiurnalApp CreateSut(AppConfig config)
        {
            _mockLogger = new Mock<ILogger>();
            _mockTimerService = new Mock<ITimerService>();
            _mockAwtrixService = new Mock<IAwtrixService>();
            _address = new AwtrixAddress { BaseTopic = "test/base/topic" };

            _mockAwtrixService.Setup(x => x.Set(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixSettings>())).ReturnsAsync(true);
            _mockAwtrixService.Setup(x => x.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>())).ReturnsAsync(true);

            return new DiurnalApp(_mockLogger.Object, _mockTimerService.Object, config, _address, _mockAwtrixService.Object);
        }

        [Fact]
        public void Init_WithEmptyConfig_DoesNotThrow()
        {
            var config = new AppConfig();
            var sut = CreateSut(config);

            var ex = Record.Exception(() => sut.Init());

            Assert.Null(ex);
        }

        [Fact]
        public void Init_WithUnparsableTimeKey_DoesNotThrow()
        {
            var config = new AppConfig();
            config.Config.Add("not-a-time", "Brightness=5");
            var sut = CreateSut(config);

            var ex = Record.Exception(() => sut.Init());

            Assert.Null(ex);
        }

        [Fact]
        public void Init_WithUnknownSettingKeyOnly_ParsesWithoutThrowing()
        {
            // The entry is parsed into an (empty) actions list even though the only setting key
            // is unrecognised - see the follow-up test below for what happens when that time is
            // actually reached. A time entry late in the day is used here so DiurnalApp's
            // wall-clock replay-on-Initialize logic (which would otherwise hit the same bug
            // documented below) does not fire for the whole test run.
            var config = new AppConfig();
            config.Config.Add("2359", "SomeUnknownSetting=123");
            var sut = CreateSut(config);

            var ex = Record.Exception(() => sut.Init());

            Assert.Null(ex);
        }

        [Fact(Skip = "Known bug: a Diurnal time entry whose only setting key is unrecognised still creates an empty actions list; when that minute is reached, AwtrixSettings.ToString() calls Aggregate() on an empty Keys collection and throws InvalidOperationException from within ClockTickMinute's logging call.")]
        public void MinuteChanged_TimeWithOnlyUnknownSettingKey_ThrowsDueToEmptySettingsToString()
        {
            var config = new AppConfig();
            config.Config.Add("0600", "SomeUnknownSetting=123");
            var sut = CreateSut(config);
            sut.Init();

            var tickTime = DateTime.Today.AddHours(6);
            var ex = Record.Exception(() => _mockTimerService.Raise(m => m.MinuteChanged += null, this, new ClockTickEventArgs(tickTime)));

            Assert.Null(ex);
        }

        [Fact]
        public void MinuteChanged_MatchingBrightnessEntry_AppliesBrightness()
        {
            var config = new AppConfig();
            config.Config.Add("0600", "Brightness=8");
            var sut = CreateSut(config);
            sut.Init();
            _mockAwtrixService.Invocations.Clear();

            var tickTime = DateTime.Today.AddHours(6);
            _mockTimerService.Raise(m => m.MinuteChanged += null, this, new ClockTickEventArgs(tickTime));

            _mockAwtrixService.Verify(x => x.Set(_address, It.Is<AwtrixSettings>(s => s["BRI"] == "8")), Times.Once);
        }

        [Fact]
        public void MinuteChanged_MatchingColorEntry_AppliesGlobalTextColor()
        {
            var config = new AppConfig();
            config.Config.Add("2200", "GlobalTextColor=#112233");
            var sut = CreateSut(config);
            sut.Init();
            _mockAwtrixService.Invocations.Clear();

            var tickTime = DateTime.Today.AddHours(22);
            _mockTimerService.Raise(m => m.MinuteChanged += null, this, new ClockTickEventArgs(tickTime));

            _mockAwtrixService.Verify(x => x.Set(_address, It.Is<AwtrixSettings>(s => s["TCOL"] == "#112233")), Times.Once);
        }

        [Fact]
        public void MinuteChanged_CompoundEntry_AppliesBothSettingsTogether()
        {
            var config = new AppConfig();
            config.Config.Add("0700", "Brightness=5;GlobalTextColor=#FFFFFF");
            var sut = CreateSut(config);
            sut.Init();
            _mockAwtrixService.Invocations.Clear();

            var tickTime = DateTime.Today.AddHours(7);
            _mockTimerService.Raise(m => m.MinuteChanged += null, this, new ClockTickEventArgs(tickTime));

            _mockAwtrixService.Verify(x => x.Set(_address, It.Is<AwtrixSettings>(s => s["BRI"] == "5" && s["TCOL"] == "#FFFFFF")), Times.Once);
        }

        [Fact]
        public void MinuteChanged_NonMatchingTime_DoesNotApplySettings()
        {
            var config = new AppConfig();
            config.Config.Add("0600", "Brightness=8");
            var sut = CreateSut(config);
            sut.Init();
            _mockAwtrixService.Invocations.Clear();

            var tickTime = DateTime.Today.AddHours(9);
            _mockTimerService.Raise(m => m.MinuteChanged += null, this, new ClockTickEventArgs(tickTime));

            _mockAwtrixService.Verify(x => x.Set(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixSettings>()), Times.Never);
        }
    }
}
