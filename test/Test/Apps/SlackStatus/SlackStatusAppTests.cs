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

        /// <summary>
        /// With injected <see cref="SlackSettings"/> the app never reads the process environment (WS7/CR-14).
        /// </summary>
        protected SlackStatusApp CreateAppWithSettings(string trackingUserId, SlackSettings? slackSettings)
        {
            var config = new SlackStatusAppConfig { Type = AppName };
            config.Config[SlackStatusApp.UserIdConfigKey] = trackingUserId;
            return new SlackStatusApp(NullLogger.Instance, config, _address, _awtrix.Object, _slack.Object, slackSettings);
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
    /// Serialises tests that mutate the process-wide AWTRIXSHARP_SLACK__USERID environment variable.
    /// DisableParallelization makes xUnit run this collection on its own, after the parallel collections.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class SlackUserIdEnvironmentCollection
    {
        public const string Name = "SlackUserIdEnvironment";
    }

    /// <summary>
    /// The only test that still touches the process environment: a hand-built app (no SlackSettings)
    /// falls back to the literal AWTRIXSHARP_SLACK__USERID variable, as before. The original value is restored.
    /// </summary>
    [Collection(SlackUserIdEnvironmentCollection.Name)]
    public class SlackStatusAppUserIdEnvironmentFallbackTests : SlackStatusAppTestBase
    {
        [Fact]
        public async Task InitAsync_BlankUserId_NoSettings_UsesLiteralEnvironmentVariable()
        {
            var previous = Environment.GetEnvironmentVariable(SlackStatusApp.UserIdEnvironmentVariable);
            Environment.SetEnvironmentVariable(SlackStatusApp.UserIdEnvironmentVariable, "U-FROM-ENV");
            try
            {
                var app = CreateApp("");

                await app.InitAsync();

                _slack.VerifyAdd(s => s.UserStatusChanged += It.IsAny<EventHandler<SlackUserStatusChangedEventArgs>>(), Times.Once);
            }
            finally
            {
                Environment.SetEnvironmentVariable(SlackStatusApp.UserIdEnvironmentVariable, previous);
            }
        }
    }

    /// <summary>
    /// The "no user id" path, with configuration injected as empty SlackSettings (WS7/CR-14), so the
    /// process environment is never read and these tests run in parallel.
    /// </summary>
    public class SlackStatusAppNoUserIdTests : SlackStatusAppTestBase
    {
        [Fact]
        public async Task InitAsync_BlankUserId_UsesSlackSettingsUserId()
        {
            var app = CreateAppWithSettings("", new SlackSettings { UserId = "U-FROM-SETTINGS" });

            await app.InitAsync();

            _slack.VerifyAdd(s => s.UserStatusChanged += It.IsAny<EventHandler<SlackUserStatusChangedEventArgs>>(), Times.Once);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task InitAsync_NoUserIdConfigured_DoesNotThrowOrSubscribe(string userId)
        {
            var app = CreateAppWithSettings(userId, new SlackSettings());

            var ex = await Record.ExceptionAsync(() => app.InitAsync());

            Assert.Null(ex);
            _slack.VerifyAdd(s => s.UserStatusChanged += It.IsAny<EventHandler<SlackUserStatusChangedEventArgs>>(), Times.Never);
        }

        [Fact]
        public async Task UserStatusChanged_AfterInitWithoutUserId_IsIgnored()
        {
            var app = CreateAppWithSettings("", new SlackSettings());
            await app.InitAsync();
            _awtrix.Invocations.Clear();

            // Not subscribed; even a directly raised event for an empty user id must publish nothing
            var ex = Record.Exception(() => _slack.Raise(s => s.UserStatusChanged += null, _slack.Object,
                new SlackUserStatusChangedEventArgs { UserId = "", StatusText = "In a meeting" }));

            Assert.Null(ex);
            Assert.Empty(_awtrix.Invocations);
        }
    }
}
