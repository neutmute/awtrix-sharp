using AwtrixSharpWeb.Apps;
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Test.Apps
{
    /// <summary>
    /// Minimal concrete app used to exercise the abstract AwtrixApp&lt;TConfig&gt; base class.
    /// </summary>
    internal class TestAwtrixApp : AwtrixApp<AppConfig>
    {
        public int InitializeCallCount { get; private set; }

        public Exception? InitializeException { get; set; }

        public TestAwtrixApp(ILogger logger, AppConfig config, AwtrixAddress awtrixAddress, IAwtrixService awtrixService)
            : base(logger, config, awtrixAddress, awtrixService)
        {
        }

        protected override void Initialize()
        {
            InitializeCallCount++;
            if (InitializeException != null)
            {
                throw InitializeException;
            }
        }

        // Public wrappers to exercise protected publish helpers directly
        public Task<bool> PublicNotify(AwtrixAppMessage message) => Notify(message);
        public Task<bool> PublicDismiss() => Dismiss();
        public Task<bool> PublicAppUpdate(AwtrixAppMessage message) => AppUpdate(message);
        public Task<bool> PublicAppClear() => AppClear();
        public Task<bool> PublicSet(AwtrixSettings settings) => Set(settings);
    }

    public class AwtrixAppTests
    {
        private Mock<ILogger> _mockLogger;
        private Mock<IAwtrixService> _mockAwtrixService;
        private AwtrixAddress _address;

        private TestAwtrixApp CreateSut(string type = null)
        {
            _mockLogger = new Mock<ILogger>();
            _mockAwtrixService = new Mock<IAwtrixService>();
            _address = new AwtrixAddress { BaseTopic = "test/base/topic" };

            _mockAwtrixService.Setup(x => x.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>())).ReturnsAsync(true);
            _mockAwtrixService.Setup(x => x.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>())).ReturnsAsync(true);
            _mockAwtrixService.Setup(x => x.Notify(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixAppMessage>())).ReturnsAsync(true);
            _mockAwtrixService.Setup(x => x.Dismiss(It.IsAny<AwtrixAddress>())).ReturnsAsync(true);
            _mockAwtrixService.Setup(x => x.Set(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixSettings>())).ReturnsAsync(true);

            var config = new AppConfig();
            if (type != null)
            {
                config.WithName(type);
            }

            return new TestAwtrixApp(_mockLogger.Object, config, _address, _mockAwtrixService.Object);
        }

        [Fact]
        public void Constructor_ExposesAddressAndConfig()
        {
            var sut = CreateSut("MyApp");

            Assert.Same(_address, sut.AwtrixAddress);
            Assert.Equal("MyApp", sut.GetConfig().Type);
            Assert.Equal("MyApp", sut.GetConfig().Name);
        }

        [Fact]
        public async Task InitAsync_WithNamedConfig_ClearsAppAndCallsInitialize()
        {
            var sut = CreateSut("MyApp");

            await sut.InitAsync();

            _mockAwtrixService.Verify(x => x.AppClear(_address, "MyApp"), Times.Once);
            Assert.Equal(1, sut.InitializeCallCount);
        }

        [Fact]
        public async Task InitAsync_WithNullName_SkipsAppClear_ButStillInitializes()
        {
            var sut = CreateSut(type: null);

            await sut.InitAsync();

            _mockAwtrixService.Verify(x => x.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>()), Times.Never);
            Assert.Equal(1, sut.InitializeCallCount);
        }

        [Fact]
        public async Task InitAsync_CalledTwice_ClearsAndInitializesOnce()
        {
            var sut = CreateSut("MyApp");

            await sut.InitAsync();
            await sut.InitAsync();

            _mockAwtrixService.Verify(x => x.AppClear(_address, "MyApp"), Times.Once);
            Assert.Equal(1, sut.InitializeCallCount);
        }

        [Fact]
        public async Task InitAsync_WhenFirstAttemptThrows_IsNotRetried()
        {
            var sut = CreateSut("MyApp");
            sut.InitializeException = new InvalidOperationException("bad config");

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.InitAsync());
            await sut.InitAsync();

            Assert.Equal(1, sut.InitializeCallCount);
        }

        [Fact]
        public async Task AppClear_WithNullConfigName_ReturnsFalseWithoutCallingService()
        {
            var sut = CreateSut(type: null);

            var result = await sut.PublicAppClear();

            Assert.False(result);
            _mockAwtrixService.Verify(x => x.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task AppClear_WithConfigName_DelegatesToService()
        {
            var sut = CreateSut("MyApp");

            var result = await sut.PublicAppClear();

            Assert.True(result);
            _mockAwtrixService.Verify(x => x.AppClear(_address, "MyApp"), Times.Once);
        }

        [Fact]
        public async Task Notify_DelegatesToServiceWithAddressAndMessage()
        {
            var sut = CreateSut("MyApp");
            var message = new AwtrixAppMessage().SetText("hello");

            var result = await sut.PublicNotify(message);

            Assert.True(result);
            _mockAwtrixService.Verify(x => x.Notify(_address, message), Times.Once);
        }

        [Fact]
        public async Task Dismiss_DelegatesToService()
        {
            var sut = CreateSut("MyApp");

            var result = await sut.PublicDismiss();

            Assert.True(result);
            _mockAwtrixService.Verify(x => x.Dismiss(_address), Times.Once);
        }

        [Fact]
        public async Task AppUpdate_DelegatesToServiceWithConfigName()
        {
            var sut = CreateSut("MyApp");
            var message = new AwtrixAppMessage().SetText("hello");

            var result = await sut.PublicAppUpdate(message);

            Assert.True(result);
            _mockAwtrixService.Verify(x => x.AppUpdate(_address, "MyApp", message), Times.Once);
        }

        [Fact]
        public async Task Set_DelegatesToServiceWithAddressAndSettings()
        {
            var sut = CreateSut("MyApp");
            var settings = new AwtrixSettings().SetBrightness(10);

            var result = await sut.PublicSet(settings);

            Assert.True(result);
            _mockAwtrixService.Verify(x => x.Set(_address, settings), Times.Once);
        }

        [Fact]
        public void Dispose_CallsAppClear()
        {
            var sut = CreateSut("MyApp");

            sut.Dispose();

            _mockAwtrixService.Verify(x => x.AppClear(_address, "MyApp"), Times.Once);
        }

        [Fact]
        public async Task DisposeAsync_AwaitsAppClearWithoutBlocking()
        {
            var sut = CreateSut("MyApp");
            var gate = new TaskCompletionSource<bool>();
            _mockAwtrixService.Setup(x => x.AppClear(_address, "MyApp")).Returns(gate.Task);

            var dispose = sut.DisposeAsync();

            Assert.False(dispose.IsCompleted);
            gate.SetResult(true);
            await dispose;
            _mockAwtrixService.Verify(x => x.AppClear(_address, "MyApp"), Times.Once);
        }

        [Fact]
        public void ExecuteNow_DefaultImplementation_DoesNotThrow()
        {
            var sut = CreateSut("MyApp");

            var ex = Record.Exception(() => sut.ExecuteNow());

            Assert.Null(ex);
        }
    }
}
