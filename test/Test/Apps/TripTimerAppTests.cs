using AwtrixSharpWeb.Apps.TripTimer;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
using AwtrixSharpWeb.Services.TripPlanner;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Test.Domain;

namespace Test.Apps
{

    public class TripTimerAppTests
    {
        IClock _clock;
        Mock<ILogger> _mockLog;
        AwtrixAddress _mockAddress;
        Mock<IAwtrixService> _mockAwtrixService;
        Mock<ITimerService> _mockTimerService;
        Mock<ITripPlannerService> _mockTripPlannerService;
        TripTimerAppConfig _timerConfig;

        public TripTimerApp GetSystemUnderTest()
        {
            _clock = new MockClock(DateTimeOffset.Now);
            _mockLog = new Mock<ILogger>();
            _mockAwtrixService = new Mock<IAwtrixService>();   
            _mockAddress = new AwtrixAddress { BaseTopic = "test/base/topic" };
            _mockTimerService = new Mock<ITimerService>();
            _mockTripPlannerService = new Mock<ITripPlannerService>();


            var baseTime = DateTimeOffset.Parse("2025-08-19T06:00:00+10:00");

            _mockTripPlannerService.Setup(x => x.GetNextDepartures(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new List<TripSummary>
                {
                    TripSummaryTests.Create(baseTime)
                    ,TripSummaryTests.Create(baseTime.AddMinutes(5))
                    ,TripSummaryTests.Create(baseTime.AddMinutes(10))
                });

            _mockLog
              .Setup(x => x.Log(
                  It.Is<LogLevel>(l => l == LogLevel.Information),
                  It.IsAny<EventId>(),
                  It.Is<It.IsAnyType>((o, t) => true), // state
                  It.IsAny<Exception>(),
                  (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
              .Callback((LogLevel logLevel, EventId eventId, object state, Exception ex, Delegate formatter) =>
              {
                  // state.ToString() contains the message for simple cases
                  Console.WriteLine(state.ToString());
              });

            _timerConfig = new TripTimerAppConfig
            {
                CronSchedule = "* * * * *", // Every minute
                ActiveTime = TimeSpan.FromMinutes(30),
                TimeToOrigin = TimeSpan.FromMinutes(14),
                TimeToPrepare = TimeSpan.FromMinutes(8),
            };

            _timerConfig.Type = "TripTimer";

            var sut = new TripTimerApp(
                _mockLog.Object
                , _clock
                , _mockAddress
                , _mockAwtrixService.Object
                , _mockTimerService.Object
                , _timerConfig
                , _mockTripPlannerService.Object);

            return sut;
        }

        [Fact]
        public void GetAlarm()
        {
            // Arrange
            var sut = GetSystemUnderTest();
            var departureTime = DateTimeOffset.Parse("2025-08-19T06:41:00+10:00");
            var actualAlarmTime = sut.GetAlarmTime(TripSummaryTests.Create(departureTime));

            var expectedPrepareForDepartTime = DateTimeOffset.Parse("2025-08-19T06:19:00+10:00");
            var expectedDepartTime = DateTimeOffset.Parse("2025-08-19T06:27:00+10:00");
            Assert.Equal(departureTime, actualAlarmTime.OriginDepartTime);
            Assert.Equal(expectedDepartTime, actualAlarmTime.DepartForOriginTime);
            Assert.Equal(expectedPrepareForDepartTime, actualAlarmTime.PrepareForDepartTime);
        }

        [Theory]
        [InlineData(0, 100, "")] // 0% progress
        [InlineData(175, 44, "")] // 25% progress
        [InlineData(150, 53, "")] // 50% progress
        [InlineData(300, 0, "")] // 100% progress
        public void GetProgress_ReturnsExpectedProgressValue(int secondsToAlarm, int expectedProgess, string rationale)
        {

            var nextAlarm = DateTimeOffset.Parse("2023-01-01T12:00:00Z");
            var now = nextAlarm.AddSeconds(-secondsToAlarm);
            var clock = new MockClock(now);

            var sut = GetSystemUnderTest();

            var actualProgess = sut.GetProgress(clock, nextAlarm);

            Assert.Equal(expectedProgess, actualProgess.quantized);
        }
        
    }
}

