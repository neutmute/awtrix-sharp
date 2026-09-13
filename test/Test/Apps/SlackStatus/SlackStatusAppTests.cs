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
    /// The "no user id" path is only reachable with the environment fallback unset (WS7/CR-14 will move
    /// that fallback to IConfiguration). The original value is restored in Dispose.
    /// </summary>
    [Collection(SlackUserIdEnvironmentCollection.Name)]
    public class SlackStatusAppNoUserIdTests : SlackStatusAppTestBase, IDisposable
    {
        private readonly string? _previousUserId;

        public SlackStatusAppNoUserIdTests()
        {
            _previousUserId = Environment.GetEnvironmentVariable(SlackStatusApp.UserIdEnvironmentVariable);
            Environment.SetEnvironmentVariable(SlackStatusApp.UserIdEnvironmentVariable, null);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(SlackStatusApp.UserIdEnvironmentVariable, _previousUserId);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task InitAsync_NoUserIdConfigured_DoesNotThrowOrSubscribe(string userId)
        {
            var app = CreateApp(userId);

            var ex = await Record.ExceptionAsync(() => app.InitAsync());

            Assert.Null(ex);
            _slack.VerifyAdd(s => s.UserStatusChanged += It.IsAny<EventHandler<SlackUserStatusChangedEventArgs>>(), Times.Never);
        }

        [Fact]
        public async Task UserStatusChanged_AfterInitWithoutUserId_IsIgnored()
        {
            var app = CreateApp("");
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
