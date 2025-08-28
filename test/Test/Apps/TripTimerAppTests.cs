using AwtrixSharpWeb.Apps;
using AwtrixSharpWeb.Apps.Configs;
using AwtrixSharpWeb.Domain;
using AwtrixSharpWeb.HostedServices;
using AwtrixSharpWeb.Interfaces;
using AwtrixSharpWeb.Services;
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

        [Fact]
        public async Task BasicLifecycle()
        {
            // Arrange
            var mockClock = new MockClock(new DateTimeOffset(2023, 1, 1, 12, 0, 0, TimeSpan.Zero));
            _clock = mockClock;
            
            var sut = GetSystemUnderTest();
            var secondEventCount = 0;
            var minuteEventCount = 0;
            
            // Configure mocks to track calls
            _mockAwtrixService.Setup(x => x.AppUpdate(It.IsAny<AwtrixAddress>(), It.IsAny<string>(), It.IsAny<AwtrixAppMessage>()))
                .ReturnsAsync(true);
                
            _mockAwtrixService.Setup(x => x.Dismiss(It.IsAny<AwtrixAddress>()))
                .ReturnsAsync(true);
            
            // Act
            sut.Init();
            
            // Simulate time passing and trigger the scheduled execution
            // First, advance to just before the next cron trigger
            mockClock.SetTime(new DateTimeOffset(2023, 1, 1, 12, 0, 50, TimeSpan.Zero));
            
            
            // Wait briefly for the app to activate based on the cron trigger
            await Task.Delay(100);
            
            // Trigger some timer events
            for (int i = 0; i < 5; i++)
            {
                var currentTime = DateTime.Now.AddSeconds(i);
                _mockTimerService.Raise(m => m.SecondChanged += null, new ClockTickEventArgs(currentTime));
                secondEventCount++;
                
                if (i % 60 == 0) // Simulate a minute change every 60 seconds
                {
                    _mockTimerService.Raise(m => m.MinuteChanged += null, new ClockTickEventArgs(currentTime));
                    minuteEventCount++;
                }
                
                await Task.Delay(50); // Give time for event processing
            }
            
            // Advance time past the active period to trigger clean-up
            mockClock.SetTime(mockClock.Now.Add(_timerConfig.ActiveTime).AddSeconds(1));
            
            // Wait for clean-up to complete
            await Task.Delay(100);
            
            // Assert
            _mockLog.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Information),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Clock ticked second")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.AtLeast(1));
                
            // Verify the app completed its lifecycle
            _mockAwtrixService.Verify(x => x.Dismiss(It.IsAny<AwtrixAddress>()), Times.AtLeastOnce);
            
            // Clean up
            sut.Dispose();
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

