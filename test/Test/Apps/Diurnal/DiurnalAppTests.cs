using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.Diurnal;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Test.Apps.Diurnal
{
    /// <summary>
    /// DiurnalApp over a mocked timer and awtrix service, with fixed dates (never DateTime.Today).
    /// Moq's completed tasks make FireAndLog run synchronously, so assertions can follow the raise directly.
    /// </summary>
    public class DiurnalAppTests
    {
        private static readonly TimeSpan Aest = TimeSpan.FromHours(10);
        private static readonly DateTime Day = new(2026, 9, 13, 0, 0, 0, DateTimeKind.Local);

        private static readonly (string Time, string Value)[] Shipped =
        {
            ("0600", "Brightness=8"),
            ("0700", "GlobalTextColor=#FFFFFF"),
            ("1900", "GlobalTextColor=#FF0000"),
            ("2100", "Brightness=1"),
        };

        private readonly Mock<ITimerService> _timer = new();
        private readonly Mock<IAwtrixService> _awtrix = new();
        private readonly AwtrixAddress _address = new() { BaseTopic = "awtrix/clock1" };
        private readonly List<AwtrixSettings> _applied = new();

        public DiurnalAppTests()
        {
            _awtrix.Setup(a => a.Set(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixSettings>()))
                .Callback<AwtrixAddress, AwtrixSettings>((_, settings) => _applied.Add(settings))
                .ReturnsAsync(true);
            _awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>())).ReturnsAsync(true);
        }

        private static DateTimeOffset At(int hour, int minute) => new(2026, 9, 13, hour, minute, 0, Aest);

        private DiurnalApp CreateApp(DateTimeOffset now, params (string Time, string Value)[] entries)
        {
            var config = AppConfig.Empty().WithName(AppNames.DiurnalApp);
            foreach (var (time, value) in entries)
            {
                config.Config[time] = value;
            }

            return new DiurnalApp(NullLogger.Instance, new MockClock(now), _timer.Object, config, _address, _awtrix.Object);
        }

        /// <summary>Init at <paramref name="now"/>, then forget the startup publish.</summary>
        private async Task<DiurnalApp> StartApp(DateTimeOffset now, params (string Time, string Value)[] entries)
        {
            var app = CreateApp(now, entries);
            await app.InitAsync();
            _applied.Clear();
            return app;
        }

        private void RaiseMinute(int hour, int minute, int second = 0, int dayOffset = 0)
        {
            _timer.Raise(t => t.MinuteChanged += null, _timer.Object,
                new ClockTickEventArgs(Day.AddDays(dayOffset).Add(new TimeSpan(hour, minute, second))));
        }

        // ---------- Init / validation (CR-21) ----------

        [Fact]
        public async Task InitAsync_EmptyConfig_DoesNotThrowSubscribeOrPublish()
        {
            var app = CreateApp(At(7, 0));

            var ex = await Record.ExceptionAsync(() => app.InitAsync());

            Assert.Null(ex);
            Assert.Empty(_applied);
            _timer.VerifyAdd(t => t.MinuteChanged += It.IsAny<EventHandler<ClockTickEventArgs>>(), Times.Never);
        }

        [Fact]
        public async Task InitAsync_UnparsableTimeKey_DoesNotThrowOrPublish()
        {
            var app = CreateApp(At(7, 0), ("not-a-time", "Brightness=5"));

            var ex = await Record.ExceptionAsync(() => app.InitAsync());

            Assert.Null(ex);
            Assert.Empty(_applied);
        }

        [Fact]
        public async Task InitAsync_UnknownSettingKeyOnly_DoesNotThrowOrPublish()
        {
            var app = CreateApp(At(7, 0), ("0600", "Brightnes=8"));

            var ex = await Record.ExceptionAsync(() => app.InitAsync());

            Assert.Null(ex);
            Assert.Empty(_applied);
        }

        [Theory]
        [InlineData("Brightness=dim")]
        [InlineData("Brightness=300")]
        public async Task InitAsync_InvalidBrightnessValue_DoesNotThrowOrPublish(string value)
        {
            var app = CreateApp(At(22, 0), ("2100", value));

            var ex = await Record.ExceptionAsync(() => app.InitAsync());

            Assert.Null(ex);
            Assert.Empty(_applied);
        }

        [Fact]
        public async Task InitAsync_MixedValidAndInvalidSettings_AppliesValidPart()
        {
            var app = CreateApp(At(22, 0), ("2100", "Brightness=dim;GlobalTextColor=#00FF00"));

            await app.InitAsync();

            var settings = Assert.Single(_applied);
            Assert.False(settings.ContainsKey("BRI"));
            Assert.Equal("#00FF00", settings["TCOL"]);
        }

        [Fact]
        public async Task MinuteTick_UnknownKeyOnly_DoesNotThrowOrPublish()
        {
            // A valid entry at another time keeps the app subscribed, so the tick path really runs
            await StartApp(At(5, 59), ("0600", "SomeUnknownSetting=123"), ("0700", "Brightness=5"));
            _timer.VerifyAdd(t => t.MinuteChanged += It.IsAny<EventHandler<ClockTickEventArgs>>(), Times.Once);

            var ex = Record.Exception(() => RaiseMinute(6, 0));

            Assert.Null(ex);
            Assert.Empty(_applied);

            RaiseMinute(7, 0);
            Assert.Equal("5", Assert.Single(Assert.Single(_applied), kv => kv.Key == "BRI").Value);
        }

        [Fact]
        public async Task MinuteTick_OutOfRangeBrightness_DoesNotThrowOrPublish()
        {
            await StartApp(At(20, 59), ("2100", "Brightness=300"), ("2200", "GlobalTextColor=#112233"));
            _timer.VerifyAdd(t => t.MinuteChanged += It.IsAny<EventHandler<ClockTickEventArgs>>(), Times.Once);

            var ex = Record.Exception(() => RaiseMinute(21, 0));

            Assert.Null(ex);
            Assert.Empty(_applied);

            RaiseMinute(22, 0);
            var settings = Assert.Single(_applied);
            Assert.Equal("#112233", settings["TCOL"]);
            Assert.False(settings.ContainsKey("BRI"));
        }

        // ---------- Startup state (CR-20) ----------

        [Fact]
        public async Task InitAsync_RestoresStateInEffect_AsOneMergedSet()
        {
            var app = CreateApp(At(7, 0), Shipped);

            await app.InitAsync();

            var settings = Assert.Single(_applied);
            Assert.Equal("8", settings["BRI"]);
            Assert.Equal("#FFFFFF", settings["TCOL"]);
            _awtrix.Verify(a => a.Set(_address, It.IsAny<AwtrixSettings>()), Times.Once);
        }

        [Fact]
        public async Task InitAsync_RestartAt0300_RestoresYesterdayEveningSettings()
        {
            var app = CreateApp(At(3, 0), Shipped);

            await app.InitAsync();

            var settings = Assert.Single(_applied);
            Assert.Equal("1", settings["BRI"]);
            Assert.Equal("#FF0000", settings["TCOL"]);
        }

        [Fact]
        public async Task MinuteTick_SameMinuteAsStartup_DoesNotReapply()
        {
            var app = CreateApp(At(6, 0), ("0600", "Brightness=8"));
            await app.InitAsync();
            Assert.Single(_applied);

            RaiseMinute(6, 0, second: 30);

            Assert.Single(_applied);
        }

        // ---------- Ticks (CR-20) ----------

        [Fact]
        public async Task MinuteTick_MatchingBrightnessEntry_AppliesBrightness()
        {
            await StartApp(At(5, 59), ("0600", "Brightness=8"));

            RaiseMinute(6, 0);

            Assert.Equal("8", Assert.Single(_applied)["BRI"]);
            _awtrix.Verify(a => a.Set(_address, It.IsAny<AwtrixSettings>()), Times.Exactly(2)); // startup + tick
        }

        [Fact]
        public async Task MinuteTick_MatchingColorEntry_AppliesGlobalTextColor()
        {
            await StartApp(At(21, 59), ("2200", "GlobalTextColor=#112233"));

            RaiseMinute(22, 0);

            Assert.Equal("#112233", Assert.Single(_applied)["TCOL"]);
        }

        [Fact]
        public async Task MinuteTick_CompoundEntry_AppliesBothSettingsTogether()
        {
            await StartApp(At(6, 59), ("0700", "Brightness=5;GlobalTextColor=#FFFFFF"));

            RaiseMinute(7, 0);

            var settings = Assert.Single(_applied);
            Assert.Equal("5", settings["BRI"]);
            Assert.Equal("#FFFFFF", settings["TCOL"]);
        }

        [Fact]
        public async Task MinuteTick_NonMatchingTime_DoesNotPublish()
        {
            await StartApp(At(8, 59), ("0600", "Brightness=8"));

            RaiseMinute(9, 0);

            Assert.Empty(_applied);
        }

        [Fact]
        public async Task MinuteTick_WithNonZeroSeconds_StillMatchesEntry()
        {
            await StartApp(At(5, 59), ("0600", "Brightness=8"));

            RaiseMinute(6, 0, second: 7);

            Assert.Equal("8", Assert.Single(_applied)["BRI"]);
        }

        [Fact]
        public async Task MinuteTick_WhenSetFaults_DoesNotPropagate()
        {
            _awtrix.Setup(a => a.Set(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixSettings>()))
                .ThrowsAsync(new HttpRequestException("device offline"));
            var app = CreateApp(At(5, 59), ("0600", "Brightness=8"));

            var initException = await Record.ExceptionAsync(() => app.InitAsync());
            var tickException = Record.Exception(() => RaiseMinute(6, 0));

            Assert.Null(initException);
            Assert.Null(tickException);
            _awtrix.Verify(a => a.Set(_address, It.IsAny<AwtrixSettings>()), Times.Exactly(2)); // startup + tick
        }

        // ---------- Publish ordering (WS5 fix round 1, m1) ----------

        /// <summary>
        /// A tick raised the instant the handler is attached (TimerService fires on its own thread) must be
        /// published after the startup restore, even while the restore is still in flight on the transport.
        /// </summary>
        [Fact]
        public async Task StartupRestore_TickRacingSubscription_IsPublishedAfterRestore()
        {
            var restoreGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondSet = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var applied = new List<AwtrixSettings>();
            _awtrix.Setup(a => a.Set(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixSettings>()))
                .Returns<AwtrixAddress, AwtrixSettings>((_, settings) =>
                {
                    lock (applied)
                    {
                        applied.Add(settings);
                        if (applied.Count == 1)
                        {
                            return restoreGate.Task;
                        }
                    }

                    secondSet.TrySetResult();
                    return Task.FromResult(true);
                });

            var tickTime = Day.Add(new TimeSpan(21, 0, 0));
            _timer.SetupAdd(t => t.MinuteChanged += It.IsAny<EventHandler<ClockTickEventArgs>>())
                .Callback<EventHandler<ClockTickEventArgs>>(handler => handler(_timer.Object, new ClockTickEventArgs(tickTime)));

            var app = CreateApp(new DateTimeOffset(2026, 9, 13, 20, 59, 59, Aest), Shipped);
            await app.InitAsync();

            lock (applied)
            {
                // Only the restore has reached the transport; the 21:00 tick waits behind it
                Assert.Single(applied);
                Assert.Equal("8", applied[0]["BRI"]);
            }

            restoreGate.SetResult(true);
            await secondSet.Task.WaitAsync(TimeSpan.FromSeconds(5));

            lock (applied)
            {
                Assert.Equal(2, applied.Count);
                Assert.Equal("1", applied[1]["BRI"]); // night brightness wins
            }
        }

        [Fact]
        public async Task MinuteTick_WhileRestoreInFlightAndRestoreFaults_TickStillPublished()
        {
            var restoreGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondSet = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            _awtrix.Setup(a => a.Set(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixSettings>()))
                .Returns<AwtrixAddress, AwtrixSettings>((_, _) =>
                {
                    if (Interlocked.Increment(ref calls) == 1)
                    {
                        return restoreGate.Task;
                    }

                    secondSet.TrySetResult();
                    return Task.FromResult(true);
                });

            var app = CreateApp(At(20, 59), Shipped);
            await app.InitAsync();

            RaiseMinute(21, 0);
            Assert.Equal(1, Volatile.Read(ref calls));

            restoreGate.SetException(new HttpRequestException("device offline"));
            await secondSet.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(2, Volatile.Read(ref calls));
        }

        [Fact]
        public async Task MinuteTick_StallAcrossEntry_AppliesMissedEntry()
        {
            await StartApp(At(20, 59), Shipped);

            RaiseMinute(21, 1); // coalesced tick: 21:00 never arrived

            var settings = Assert.Single(_applied);
            Assert.Equal("1", Assert.Single(settings, kv => kv.Key == "BRI").Value);
            Assert.Single(settings);
        }

        [Fact]
        public async Task MinuteTick_CrossingMidnight_AppliesMidnightEntry()
        {
            await StartApp(At(23, 59), ("0000", "Brightness=3"));

            RaiseMinute(0, 1, dayOffset: 1);

            Assert.Equal("3", Assert.Single(_applied)["BRI"]);
        }

        [Fact]
        public async Task MinuteTick_ClockMovesBackwards_SkipsThenResumesFromNewTime()
        {
            await StartApp(At(3, 0), ("0230", "Brightness=2"));

            RaiseMinute(2, 0); // DST end / NTP step back
            Assert.Empty(_applied);

            RaiseMinute(2, 30);
            Assert.Equal("2", Assert.Single(_applied)["BRI"]);
        }

        [Fact]
        public async Task MinuteTick_AfterGapOfDays_AppliesStateAtTickTime()
        {
            await StartApp(At(0, 30), ("0600", "Brightness=8"), ("2100", "Brightness=1"));

            RaiseMinute(7, 0, dayOffset: 3); // host suspended for days

            Assert.Equal("8", Assert.Single(_applied)["BRI"]);
        }
    }
}
