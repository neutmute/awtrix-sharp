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
    /// ClockTickSecond runs on the timer loop; it must never let an exception escape or wait on teardown I/O.
    /// </summary>
    public class TripTimerAppTickTests
    {
        private static readonly DateTimeOffset Departure = DateTimeOffset.Parse("2025-08-19T06:41:00+10:00");

        private static (TripTimerApp app, Mock<IAwtrixService> awtrix) Create(DateTimeOffset now)
        {
            var awtrix = new Mock<IAwtrixService>();
            awtrix.Setup(a => a.Notify(It.IsAny<AwtrixAddress>(), It.IsAny<AwtrixAppMessage>())).ReturnsAsync(true);
            awtrix.Setup(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>())).ReturnsAsync(true);
            awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>())).ReturnsAsync(true);
            var planner = new Mock<ITripPlannerService>();
            planner.Setup(p => p.GetNextDepartures(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new List<TripSummary> { TripSummaryTests.Create(Departure) });
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
                planner.Object);

            app.NextDepartures.Add(TripSummaryTests.Create(Departure));
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
            var (app, awtrix) = Create(now);
            awtrix.Setup(a => a.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()))
                .ThrowsAsync(new HttpRequestException("device offline"));

            var exception = Record.Exception(() => InvokeClockTickSecond(app, now.DateTime));

            Assert.Null(exception);
        }

        [Fact]
        public void SecondTick_NoFutureDeparturesWhileNotActive_DoesNotPropagate()
        {
            var now = Departure.AddMinutes(5);
            var (app, _) = Create(now);

            var exception = Record.Exception(() => InvokeClockTickSecond(app, now.DateTime));

            Assert.Null(exception);
        }

        [Fact]
        public async Task SecondTick_NoFutureDeparturesWhileActive_ReturnsPromptlyWhenAppClearIsSlow()
        {
            var now = Departure.AddMinutes(5);
            var (app, awtrix) = Create(now);
            var clearIsSlow = false;
            var slowClear = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            awtrix.Setup(a => a.AppClear(It.IsAny<AwtrixAddress>(), It.IsAny<string>()))
                .Returns(() => clearIsSlow ? slowClear.Task : Task.FromResult(true));
            app.ExecuteNow();
            clearIsSlow = true;

            try
            {
                await Task.Run(() => InvokeClockTickSecond(app, now.DateTime)).WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                slowClear.TrySetResult(true);
            }

            await app.LastRun.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
