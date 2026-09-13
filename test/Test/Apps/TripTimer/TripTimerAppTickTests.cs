using System.Reflection;
using AwtrixSharpWeb.Apps.TripTimer;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Test.Domain;

namespace Test.Apps.TripTimer
{
    /// <summary>
    /// ClockTickSecond runs on the timer loop; it must never let an exception escape.
    /// Invoked via reflection (private handler), matching TripTimerAppBoundaryTests' style.
    /// </summary>
    public class TripTimerAppTickTests
    {
        private static readonly DateTimeOffset Departure = DateTimeOffset.Parse("2025-08-19T06:41:00+10:00");

        private static (TripTimerApp app, Mock<IAwtrixService> awtrix) Create(DateTimeOffset now, CancellationTokenSource cts)
        {
            var awtrix = new Mock<IAwtrixService>();
            var config = new TripTimerAppConfig
            {
                CronSchedule = "* * * * *",
                ActiveTime = TimeSpan.FromMinutes(30),
                TimeToOrigin = TimeSpan.Zero,
                TimeToPrepare = TimeSpan.Zero,
            };
            config.Type = AppNames.TripTimerApp;

            var app = new TripTimerApp(
                NullLogger.Instance,
                new MockClock(now),
                new AwtrixAddress { BaseTopic = "awtrix/clock1" },
                awtrix.Object,
                new Mock<ITimerService>().Object,
                config,
                new Mock<ITripPlannerService>().Object);

            app.NextDepartures.Add(TripSummaryTests.Create(Departure));

            var ctsField = typeof(TripTimerApp).GetField("_cts", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            ctsField!.SetValue(app, cts);

            return (app, awtrix);
        }

        private static void InvokeClockTickSecond(TripTimerApp app, DateTime time)
        {
            var method = typeof(TripTimerApp).GetMethod("ClockTickSecond", BindingFlags.NonPublic | BindingFlags.Instance);
            method!.Invoke(app, new object?[] { null, new ClockTickEventArgs(time) });
        }

        [Fact]
        public void SecondTick_WhenAppUpdateFaults_DoesNotPropagate()
        {
            var now = Departure.AddMinutes(-3);
            var (app, awtrix) = Create(now, new CancellationTokenSource());
            awtrix.Setup(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()))
                .ThrowsAsync(new HttpRequestException("device offline"));

            var exception = Record.Exception(() => InvokeClockTickSecond(app, now.DateTime));

            Assert.Null(exception);
            awtrix.Verify(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()), Times.Once);
        }

        [Fact]
        public void SecondTick_NoFutureDeparturesWithDisposedCts_DoesNotPropagate()
        {
            var now = Departure.AddMinutes(5);
            var disposed = new CancellationTokenSource();
            disposed.Dispose();
            var (app, _) = Create(now, disposed);

            var exception = Record.Exception(() => InvokeClockTickSecond(app, now.DateTime));

            Assert.Null(exception);
        }

        [Fact]
        public async Task SecondTick_NoFutureDeparturesWhileActive_ReturnsPromptlyWhenAppClearIsSlow()
        {
            var now = Departure.AddMinutes(5);
            var cts = new CancellationTokenSource();
            var (app, awtrix) = Create(now, cts);
            var timer = new Mock<ITimerService>();
            var planner = new Mock<ITripPlannerService>();
            planner.Setup(p => p.GetNextDepartures(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new List<TripSummary> { TripSummaryTests.Create(Departure) });
            typeof(TripTimerApp).GetField("_timerService", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, timer.Object);
            typeof(TripTimerApp).GetField("_tripPlanner", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, planner.Object);

            var appClearGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            awtrix.Setup(a => a.Notify(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixAppMessage>())).ReturnsAsync(true);
            awtrix.Setup(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>())).ReturnsAsync(true);
            awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>())).Returns(appClearGate.Task);

            var activate = typeof(TripTimerApp).GetMethod("ActivateScheduledWork", BindingFlags.NonPublic | BindingFlags.Instance)!;
            // Start on the thread pool (no SynchronizationContext, as in production) so the
            // cancellation continuation is free to run inline on the cancelling thread.
            var activation = await Task.Factory.StartNew(
                () => (Task)activate.Invoke(app, new object[] { cts })!,
                CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
            Assert.False(activation.IsCompleted, "activation should be waiting for cancellation");

            // The tick (timer loop thread) cancels the activation; it must not wait on the slow AppClear.
            var tick = Task.Run(() => InvokeClockTickSecond(app, now.DateTime));
            try
            {
                var winner = await Task.WhenAny(tick, Task.Delay(TimeSpan.FromSeconds(5)));
                Assert.Same(tick, winner);
                Assert.True(cts.IsCancellationRequested);
            }
            finally
            {
                appClearGate.TrySetResult(true);
            }

            await activation.WaitAsync(TimeSpan.FromSeconds(5));
            awtrix.Verify(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>()), Times.Once);
        }
    }
}
