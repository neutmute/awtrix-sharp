using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Apps.SlackStatus;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Test.Apps.SlackStatus
{
    /// <summary>
    /// Shared mocks for SlackStatusApp tests. Moq's completed tasks make FireAndLog run synchronously,
    /// so assertions can follow the raise directly.
    /// </summary>
    public abstract class SlackStatusAppTestBase
    {
        protected const string AppName = "SlackStatusApp";

        protected readonly Mock<ISlackConnector> _slack = new();
        protected readonly Mock<IAwtrixService> _awtrix = new();
        protected readonly AwtrixAddress _address = new() { BaseTopic = "awtrix/clock1" };
        protected readonly List<AwtrixAppMessage> _published = new();

        protected SlackStatusAppTestBase()
        {
            _awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>())).ReturnsAsync(true);
            _awtrix.Setup(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()))
                .Callback<AwtrixAddress, string, AwtrixAppMessage>((_, _, message) => _published.Add(message))
                .ReturnsAsync(true);
        }

        protected SlackStatusApp CreateApp(string trackingUserId, params ValueMap[] valueMaps)
        {
            var config = new SlackStatusAppConfig { Type = AppName };
            config.Config[SlackStatusApp.UserIdConfigKey] = trackingUserId;
            config.ValueMaps = valueMaps.ToList();
            return new SlackStatusApp(NullLogger.Instance, config, _address, _awtrix.Object, _slack.Object);
        }
    }

    /// <summary>
    /// SlackStatusApp over mocked ISlackConnector / IAwtrixService. Every app here has a configured
    /// SlackUserId, so the AWTRIXSHARP_SLACK__USERID fallback is never read.
    /// </summary>
    public class SlackStatusAppTests : SlackStatusAppTestBase
    {
        /// <summary>Init with a tracked user id, then forget the init-time AppClear.</summary>
        private async Task<SlackStatusApp> StartApp(string trackingUserId = "U123", params ValueMap[] valueMaps)
        {
            var app = CreateApp(trackingUserId, valueMaps);
            await app.InitAsync();
            _awtrix.Invocations.Clear();
            _published.Clear();
            return app;
        }

        private void Raise(SlackUserStatusChangedEventArgs args)
        {
            _slack.Raise(s => s.UserStatusChanged += null, _slack.Object, args);
        }

        [Fact]
        public async Task InitAsync_WithUserId_SubscribesOnce()
        {
            var app = CreateApp("U123");

            await app.InitAsync();

            _slack.VerifyAdd(s => s.UserStatusChanged += It.IsAny<EventHandler<SlackUserStatusChangedEventArgs>>(), Times.Once);
        }

        [Fact]
        public async Task UserStatusChanged_ForDifferentUser_IsIgnored()
        {
            await StartApp("U123");

            var ex = Record.Exception(() => Raise(new SlackUserStatusChangedEventArgs { UserId = "SomeoneElse", StatusText = "In a meeting" }));

            Assert.Null(ex);
            Assert.Empty(_awtrix.Invocations);
        }

        [Fact]
        public async Task UserStatusChanged_NullUserId_IsIgnored()
        {
            await StartApp("U123");

            var ex = Record.Exception(() => Raise(new SlackUserStatusChangedEventArgs { UserId = null!, StatusText = "In a meeting" }));

            Assert.Null(ex);
            Assert.Empty(_awtrix.Invocations);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public async Task UserStatusChanged_TrackedUserEmptyOrNullStatus_ClearsAndDoesNotPublish(string? statusText)
        {
            await StartApp("U123");

            Raise(new SlackUserStatusChangedEventArgs { UserId = "U123", StatusText = statusText!, StatusEmoji = ":coffee:" });

            _awtrix.Verify(a => a.AppClear(_address, AppName), Times.Once);
            _awtrix.Verify(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()), Times.Never);
        }

        [Fact]
        public async Task UserStatusChanged_TrackedUserTextWithoutMap_PublishesTextWithDefaultDuration()
        {
            await StartApp("U123");

            Raise(new SlackUserStatusChangedEventArgs { UserId = "U123", StatusText = "In a meeting", StatusEmoji = null! });

            var message = Assert.Single(_published);
            Assert.Equal("In a meeting", message.Text);
            Assert.Equal("50", message["duration"]);
            _awtrix.Verify(a => a.AppUpdate(_address, AppName, It.IsAny<AwtrixAppMessage>()), Times.Once);
        }

        [Fact]
        public async Task UserStatusChanged_TextMatchesValueMap_DecoratesMessage()
        {
            var busy = new ValueMap { { "ValueMatcher", "busy" }, { "Text", "Busy" }, { "Icon", "38789" } };
            await StartApp("U123", busy);

            Raise(new SlackUserStatusChangedEventArgs { UserId = "U123", StatusText = "very busy", StatusEmoji = ":x:" });

            var message = Assert.Single(_published);
            Assert.Equal("Busy", message.Text);
            Assert.Equal("38789", message["icon"]);
        }

        [Fact]
        public async Task UserStatusChanged_EmojiMatchesValueMapWhenTextDoesNot_DecoratesMessage()
        {
            var calendar = new ValueMap { { "ValueMatcher", ":spiral_calendar_pad:" }, { "Icon", "1234" } };
            await StartApp("U123", calendar);

            Raise(new SlackUserStatusChangedEventArgs { UserId = "U123", StatusText = "Planning", StatusEmoji = ":spiral_calendar_pad:" });

            Assert.Equal("1234", Assert.Single(_published)["icon"]);
        }

        [Fact]
        public async Task UserStatusChanged_WhenAppUpdateFaults_DoesNotPropagate()
        {
            _awtrix.Setup(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()))
                .ThrowsAsync(new HttpRequestException("device offline"));
            await StartApp("U123");

            var ex = Record.Exception(() => Raise(new SlackUserStatusChangedEventArgs { UserId = "U123", StatusText = "In a meeting" }));

            Assert.Null(ex);
        }

        // ---------- Publish ordering (WS5 fix round 1, m2) ----------

        [Fact]
        public async Task UserStatusChanged_ClearWhileUpdateInFlight_ClearRunsAfterUpdateCompletes()
        {
            await StartApp("U123");
            var updateGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var order = new List<string>();
            _awtrix.Setup(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()))
                .Returns(() => { lock (order) { order.Add("update"); } return updateGate.Task; });
            _awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>()))
                .Returns(() => { lock (order) { order.Add("clear"); } cleared.TrySetResult(); return Task.FromResult(true); });

            Raise(new SlackUserStatusChangedEventArgs { UserId = "U123", StatusText = "Busy" });
            Raise(new SlackUserStatusChangedEventArgs { UserId = "U123", StatusText = "" });

            lock (order)
            {
                // The raise returned without blocking, and the clear has not overtaken the in-flight update
                Assert.Equal(new[] { "update" }, order);
            }

            updateGate.SetResult(true);
            await cleared.Task.WaitAsync(TimeSpan.FromSeconds(5));

            lock (order)
            {
                Assert.Equal(new[] { "update", "clear" }, order);
            }
        }

        [Fact]
        public async Task UserStatusChanged_SupersededQueuedStatus_IsSkippedLatestWins()
        {
            await StartApp("U123");
            var updateGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var texts = new List<string?>();
            _awtrix.Setup(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()))
                .Returns<AwtrixAddress, string, AwtrixAppMessage>((_, _, message) => { lock (texts) { texts.Add(message.Text); } return updateGate.Task; });
            _awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>()))
                .Returns(() => { cleared.TrySetResult(); return Task.FromResult(true); });

            Raise(new SlackUserStatusChangedEventArgs { UserId = "U123", StatusText = "Busy" });
            Raise(new SlackUserStatusChangedEventArgs { UserId = "U123", StatusText = "Lunch" });
            Raise(new SlackUserStatusChangedEventArgs { UserId = "U123", StatusText = "" });

            updateGate.SetResult(true);
            await cleared.Task.WaitAsync(TimeSpan.FromSeconds(5));

            lock (texts)
            {
                Assert.Equal(new[] { "Busy" }, texts); // "Lunch" was superseded before it ran
            }
            _awtrix.Verify(a => a.AppClear(_address, AppName), Times.Once);
        }

        [Fact]
        public async Task UserStatusChanged_InFlightUpdateFaults_LaterStatusStillPublished()
        {
            await StartApp("U123");
            var updateGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _awtrix.Setup(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()))
                .Returns(() => updateGate.Task);
            _awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>()))
                .Returns(() => { cleared.TrySetResult(); return Task.FromResult(true); });

            Raise(new SlackUserStatusChangedEventArgs { UserId = "U123", StatusText = "Busy" });
            Raise(new SlackUserStatusChangedEventArgs { UserId = "U123", StatusText = "" });
            _awtrix.Verify(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>()), Times.Never);

            updateGate.SetException(new HttpRequestException("device offline"));
            await cleared.Task.WaitAsync(TimeSpan.FromSeconds(5));

            _awtrix.Verify(a => a.AppClear(_address, AppName), Times.Once);
        }
    }

    /// <summary>
    /// The user id resolution path production uses (Conductor has already applied Slack:UserId, see
    /// ConductorSlackSettingsTests and SlackWiringTests): Config:SlackUserId, else the literal AWTRIXSHARP_SLACK__USERID
    /// variable. Each test sets that variable explicitly (restored afterwards) in the non-parallel environment collection.
    /// </summary>
    [Collection(ProcessEnvironmentCollection.Name)]
    public class SlackStatusAppUserIdTests : SlackStatusAppTestBase
    {
        private const string Variable = SlackStatusApp.UserIdEnvironmentVariable;

        private void Raise(string userId, string statusText) =>
            _slack.Raise(s => s.UserStatusChanged += null, _slack.Object,
                new SlackUserStatusChangedEventArgs { UserId = userId, StatusText = statusText });

        [Fact]
        public Task InitAsync_BlankUserId_TracksUserFromLiteralEnvironmentVariable() =>
            ProcessEnvironmentCollection.WithVariable(Variable, "U-FROM-ENV", async () =>
            {
                var app = CreateApp("");
                await app.InitAsync();

                Raise("U-FROM-ENV", "In a meeting");

                Assert.Equal("In a meeting", Assert.Single(_published).Text);
            });

        [Fact]
        public Task InitAsync_ConfiguredUserId_WinsOverEnvironmentVariable() =>
            ProcessEnvironmentCollection.WithVariable(Variable, "U-FROM-ENV", async () =>
            {
                var app = CreateApp("U-FROM-CONFIG");
                await app.InitAsync();

                Raise("U-FROM-ENV", "Ignored");
                Raise("U-FROM-CONFIG", "In a meeting");

                Assert.Equal("In a meeting", Assert.Single(_published).Text);
            });

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public Task InitAsync_NoUserIdConfigured_DoesNotThrowOrSubscribe(string userId) =>
            ProcessEnvironmentCollection.WithVariable(Variable, null, async () =>
            {
                var app = CreateApp(userId);

                var ex = await Record.ExceptionAsync(() => app.InitAsync());

                Assert.Null(ex);
                _slack.VerifyAdd(s => s.UserStatusChanged += It.IsAny<EventHandler<SlackUserStatusChangedEventArgs>>(), Times.Never);
            });

        [Fact]
        public Task UserStatusChanged_AfterInitWithoutUserId_IsIgnored() =>
            ProcessEnvironmentCollection.WithVariable(Variable, null, async () =>
            {
                var app = CreateApp("");
                await app.InitAsync();
                _awtrix.Invocations.Clear();

                // Not subscribed; even a directly raised event for an empty user id must publish nothing
                var ex = Record.Exception(() => Raise("", "In a meeting"));

                Assert.Null(ex);
                Assert.Empty(_awtrix.Invocations);
            });
    }
}
