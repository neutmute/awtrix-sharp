using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.Diurnal;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Test.Apps.Diurnal
{
    /// <summary>
    /// DiurnalApp takes an injected IClock for its startup replay, so time is fully controlled.
    /// Tick handling is driven via ITimerService.MinuteChanged; Moq's completed-task defaults make
    /// FireAndLog run synchronously, so verification can follow the raise directly.
    /// </summary>
    public class DiurnalAppTests
    {
        private static readonly TimeSpan Aest = TimeSpan.FromHours(10);

        private readonly Mock<ITimerService> _timer = new();
        private readonly Mock<IAwtrixService> _awtrix = new();
        private readonly AwtrixAddress _address = new() { BaseTopic = "awtrix/clock1" };

        private DiurnalApp CreateApp(DateTimeOffset now, params (string Time, string Value)[] entries)
        {
            var config = AppConfig.Empty().WithName(AppNames.DiurnalApp);
            foreach (var (time, value) in entries)
            {
                config.Config[time] = value;
            }

            return new DiurnalApp(NullLogger.Instance, new MockClock(now), _timer.Object, config, _address, _awtrix.Object);
        }

        /// <summary>
        /// Clock at midnight so no entry is earlier than "now" and the startup replay is a no-op.
        /// </summary>
        private DiurnalApp CreateSut(AppConfig config)
        {
            _awtrix.Setup(x => x.Set(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixSettings>())).ReturnsAsync(true);
            _awtrix.Setup(x => x.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>())).ReturnsAsync(true);

            return new DiurnalApp(new Mock<ILogger>().Object, new MockClock(At(0, 0)), _timer.Object, config, _address, _awtrix.Object);
        }

        private static DateTimeOffset At(int hour, int minute) => new DateTimeOffset(2026, 9, 13, hour, minute, 0, Aest);

        private void RaiseMinute(int hour, int minute, int second = 0)
        {
            _timer.Raise(t => t.MinuteChanged += null,
                new ClockTickEventArgs(new DateTime(2026, 9, 13, hour, minute, second, DateTimeKind.Local)));
        }

        private static bool HasBrightness(AwtrixSettings s, string value) => s.ContainsKey("BRI") && s["BRI"] == value;

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
            var config = new AppConfig();
            config.Config.Add("2359", "SomeUnknownSetting=123");
            var sut = CreateSut(config);

            var ex = Record.Exception(() => sut.Init());

            Assert.Null(ex);
        }

        [Fact]
        public void MinuteChanged_TimeWithOnlyUnknownSettingKey_DoesNotThrowAndSkipsSet()
        {
            // Previously a known bug: the empty settings reached AwtrixSettings.ToString() (Aggregate on
            // empty) inside ClockTickMinute and threw on the timer thread (CR-01). Now skipped with a warning.
            var config = new AppConfig();
            config.Config.Add("0600", "SomeUnknownSetting=123");
            var sut = CreateSut(config);
            sut.Init();

            var tickTime = DateTime.Today.AddHours(6);
            var ex = Record.Exception(() => _timer.Raise(m => m.MinuteChanged += null, this, new ClockTickEventArgs(tickTime)));

            Assert.Null(ex);
            _awtrix.Verify(x => x.Set(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixSettings>()), Times.Never);
        }

        [Fact]
        public void MinuteChanged_MatchingBrightnessEntry_AppliesBrightness()
        {
            var config = new AppConfig();
            config.Config.Add("0600", "Brightness=8");
            var sut = CreateSut(config);
            sut.Init();
            _awtrix.Invocations.Clear();

            var tickTime = DateTime.Today.AddHours(6);
            _timer.Raise(m => m.MinuteChanged += null, this, new ClockTickEventArgs(tickTime));

            _awtrix.Verify(x => x.Set(_address, It.Is<AwtrixSettings>(s => s["BRI"] == "8")), Times.Once);
        }

        [Fact]
        public void MinuteChanged_MatchingColorEntry_AppliesGlobalTextColor()
        {
            var config = new AppConfig();
            config.Config.Add("2200", "GlobalTextColor=#112233");
            var sut = CreateSut(config);
            sut.Init();
            _awtrix.Invocations.Clear();

            var tickTime = DateTime.Today.AddHours(22);
            _timer.Raise(m => m.MinuteChanged += null, this, new ClockTickEventArgs(tickTime));

            _awtrix.Verify(x => x.Set(_address, It.Is<AwtrixSettings>(s => s["TCOL"] == "#112233")), Times.Once);
        }

        [Fact]
        public void MinuteChanged_CompoundEntry_AppliesBothSettingsTogether()
        {
            var config = new AppConfig();
            config.Config.Add("0700", "Brightness=5;GlobalTextColor=#FFFFFF");
            var sut = CreateSut(config);
            sut.Init();
            _awtrix.Invocations.Clear();

            var tickTime = DateTime.Today.AddHours(7);
            _timer.Raise(m => m.MinuteChanged += null, this, new ClockTickEventArgs(tickTime));

            _awtrix.Verify(x => x.Set(_address, It.Is<AwtrixSettings>(s => s["BRI"] == "5" && s["TCOL"] == "#FFFFFF")), Times.Once);
        }

        [Fact]
        public void MinuteChanged_NonMatchingTime_DoesNotApplySettings()
        {
            var config = new AppConfig();
            config.Config.Add("0600", "Brightness=8");
            var sut = CreateSut(config);
            sut.Init();
            _awtrix.Invocations.Clear();

            var tickTime = DateTime.Today.AddHours(9);
            _timer.Raise(m => m.MinuteChanged += null, this, new ClockTickEventArgs(tickTime));

            _awtrix.Verify(x => x.Set(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixSettings>()), Times.Never);
        }

        [Fact]
        public void MinuteTick_MatchingEntry_AppliesSettings()
        {
            var app = CreateApp(At(0, 30), ("0600", "Brightness=8"));
            app.Init();

            RaiseMinute(6, 0);

            _awtrix.Verify(a => a.Set(_address, It.Is<AwtrixSettings>(s => HasBrightness(s, "8"))), Times.Once);
        }

        [Fact]
        public void MinuteTick_WithNonZeroSeconds_StillMatchesEntry()
        {
            var app = CreateApp(At(0, 30), ("0600", "Brightness=8"));
            app.Init();

            RaiseMinute(6, 0, second: 7);

            _awtrix.Verify(a => a.Set(_address, It.IsAny<AwtrixSettings>()), Times.Once);
        }

        [Fact]
        public void MinuteTick_WhenSetFaults_DoesNotPropagate()
        {
            _awtrix.Setup(a => a.Set(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixSettings>()))
                .ThrowsAsync(new HttpRequestException("device offline"));
            var app = CreateApp(At(0, 30), ("0600", "Brightness=8"));
            app.Init();

            var exception = Record.Exception(() => RaiseMinute(6, 0));

            Assert.Null(exception);
        }

        [Fact]
        public void MinuteTick_OutOfRangeBrightness_DoesNotThrowAndDoesNotPublish()
        {
            var app = CreateApp(At(0, 30), ("2100", "Brightness=300"));
            app.Init();

            var exception = Record.Exception(() => RaiseMinute(21, 0));

            Assert.Null(exception);
            _awtrix.Verify(a => a.Set(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixSettings>()), Times.Never);
        }

        [Fact]
        public void MinuteTick_UnknownKeyOnly_SkipsSetWithoutThrowing()
        {
            var app = CreateApp(At(0, 30), ("0600", "Brightnes=8"));
            app.Init();

            var exception = Record.Exception(() => RaiseMinute(6, 0));

            Assert.Null(exception);
            _awtrix.Verify(a => a.Set(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixSettings>()), Times.Never);
        }

        [Fact]
        public void Init_AfterEntryWithUnknownKey_StartupReplayDoesNotThrow()
        {
            var app = CreateApp(At(7, 0), ("0600", "Brightnes=8"));

            var exception = Record.Exception(() => app.Init());

            Assert.Null(exception);
            _awtrix.Verify(a => a.Set(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixSettings>()), Times.Never);
        }

        [Fact]
        public void Init_ReplaysEarlierEntriesUsingInjectedClock()
        {
            var app = CreateApp(At(7, 0), ("0600", "Brightness=8"), ("2100", "Brightness=1"));

            app.Init();

            _awtrix.Verify(a => a.Set(_address, It.Is<AwtrixSettings>(s => HasBrightness(s, "8"))), Times.Once);
            _awtrix.Verify(a => a.Set(_address, It.Is<AwtrixSettings>(s => HasBrightness(s, "1"))), Times.Never);
        }

        [Fact]
        public void Init_ReplayWithPublishesCompletingOutOfOrder_FinalAppliedValueIsLatestEntry()
        {
            // HTTP device restarting at 22:00: the 21:00 night brightness must win even if the
            // device acknowledges requests in reverse order.
            var pending = new List<(TaskCompletionSource<bool> Tcs, AwtrixSettings Settings)>();
            var applied = new List<AwtrixSettings>();
            _awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>())).ReturnsAsync(true);
            _awtrix.Setup(a => a.Set(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixSettings>()))
                .Returns((AwtrixAddress _, AwtrixSettings s) =>
                {
                    var tcs = new TaskCompletionSource<bool>();
                    var snapshot = new AwtrixSettings();
                    foreach (var kv in s) snapshot[kv.Key] = kv.Value;
                    pending.Add((tcs, snapshot));
                    return tcs.Task;
                });

            var app = CreateApp(At(22, 0), ("0600", "Brightness=80"), ("2100", "Brightness=1"));
            app.Init();

            // Complete the most recently issued publish first, repeatedly, until nothing is in flight.
            for (var guard = 0; guard < 10 && pending.Count > 0; guard++)
            {
                var last = pending[^1];
                pending.RemoveAt(pending.Count - 1);
                applied.Add(last.Settings);
                last.Tcs.SetResult(true);
            }

            Assert.Empty(pending);
            Assert.NotEmpty(applied);
            Assert.True(HasBrightness(applied[^1], "1"), $"final applied settings were {applied[^1]}");
        }
    }
}
