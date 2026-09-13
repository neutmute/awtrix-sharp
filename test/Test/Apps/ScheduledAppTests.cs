using AwtrixSharpWeb.Apps;
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Test.Apps
{
    /// <summary>
    /// Minimal concrete scheduled app used to exercise ScheduledApp&lt;TConfig&gt;.
    /// ActivateScheduledWork completes immediately unless configured to wait for cancellation,
    /// which lets tests avoid depending on the real cron wake-up (real Task.Delay) path.
    /// </summary>
    internal class TestScheduledApp : ScheduledApp<ScheduledAppConfig>
    {
        public int ActivateCallCount { get; private set; }
        public bool WaitForCancellationOnActivate { get; set; }

        public TestScheduledApp(ILogger logger, IClock clock, AwtrixAddress awtrixAddress, IAwtrixService awtrixService, ScheduledAppConfig config)
            : base(logger, clock, awtrixAddress, awtrixService, config)
        {
        }

        protected override async Task ActivateScheduledWork(CancellationTokenSource cts)
        {
            ActivateCallCount++;
            if (WaitForCancellationOnActivate)
            {
                await WaitForCancellation(cts.Token);
            }
        }

        public static Task PublicWaitForCancellation(CancellationToken token) => WaitForCancellation(token);
    }

    public class ScheduledAppTests
    {
        private Mock<ILogger> _mockLogger;
        private Mock<IAwtrixService> _mockAwtrixService;
        private AwtrixAddress _address;
        private MockClock _clock;

        private TestScheduledApp CreateSut(string cronSchedule = "* * * * *", string type = null)
        {
            _mockLogger = new Mock<ILogger>();
            _mockAwtrixService = new Mock<IAwtrixService>();
            _address = new AwtrixAddress { BaseTopic = "test/base/topic" };
            _clock = new MockClock(DateTimeOffset.Now);

            _mockAwtrixService.Setup(x => x.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>())).ReturnsAsync(true);
            _mockAwtrixService.Setup(x => x.Dismiss(It.IsAny<AwtrixAddress>())).ReturnsAsync(true);

            var config = new ScheduledAppConfig
            {
                CronSchedule = cronSchedule,
                ActiveTime = TimeSpan.FromMinutes(5)
            };
            if (type != null)
            {
                config.WithName(type);
            }

            return new TestScheduledApp(_mockLogger.Object, _clock, _address, _mockAwtrixService.Object, config);
        }

        [Fact]
        public void Init_WithValidCron_DoesNotThrow()
        {
            var sut = CreateSut();

            var ex = Record.Exception(() => sut.Init());

            Assert.Null(ex);
            sut.Dispose();
        }

        [Fact]
        public void Init_WithInvalidCron_Throws()
        {
            var sut = CreateSut(cronSchedule: "not a cron expression");

            Assert.ThrowsAny<Exception>(() => sut.Init());
        }

        [Fact]
        public async Task ExecuteNow_InvokesActivateScheduledWork()
        {
            var sut = CreateSut(type: "MyApp");

            sut.ExecuteNow();

            // Allow the fire-and-forget WakeUp() task to progress past its synchronous prefix.
            await Task.Delay(50);

            Assert.True(sut.ActivateCallCount >= 1);

            sut.Dispose();
        }

        [Fact]
        public void Dispose_CancelsPendingWorkAndClearsApp()
        {
            var sut = CreateSut(type: "MyApp");
            sut.WaitForCancellationOnActivate = true;

            sut.ExecuteNow();

            sut.Dispose();

            _mockAwtrixService.Verify(x => x.Dismiss(_address), Times.AtLeastOnce);
        }

        [Fact]
        public async Task WaitForCancellation_CompletesOnlyAfterTokenIsCancelled()
        {
            using var cts = new CancellationTokenSource();

            var task = TestScheduledApp.PublicWaitForCancellation(cts.Token);

            Assert.False(task.IsCompleted);

            cts.Cancel();

            var completed = await Task.WhenAny(task, Task.Delay(1000));
            Assert.Same(task, completed);
        }
    }
}
