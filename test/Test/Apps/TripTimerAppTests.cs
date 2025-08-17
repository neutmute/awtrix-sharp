using AwtrixSharpWeb.Apps;
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

namespace Test.Apps
{

    public class MockClock : IClock
    {
        private DateTimeOffset _currentTime;
        public MockClock(DateTimeOffset initialTime)
        {
            _currentTime = initialTime;
        }
        public DateTimeOffset Now => _currentTime;

        public void SetTime(DateTimeOffset newTime)
        {
            _currentTime = newTime;
        }

        public void AdvanceTime(TimeSpan timeSpan)
        {
            _currentTime = _currentTime.Add(timeSpan);
        }
    }   

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
                ActiveTime = TimeSpan.FromSeconds(10), // Active for 60 minutes
                TimeToOrigin = TimeSpan.FromMinutes(15), // 15 minutes to origin    
                TimeToPrepare = TimeSpan.FromMinutes(30), // 30 minutes to prepare  
            };

            _timerConfig.Add("Name", "TripTimer");

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
        [InlineData(175, 41, "")] // 25% progress
        [InlineData(150, 50, "")] // 50% progress
        [InlineData(300, 0, "")] // 100% progress
        public void GetProgress_ReturnsExpectedProgressValue(int secondsToAlarm, int expectedProgess, string rationale)
        {

            var nextAlarm = DateTimeOffset.Parse("2023-01-01T12:00:00Z");
            var now = nextAlarm.AddSeconds(-secondsToAlarm);
            var clock = new MockClock(now);

            var actualProgess = TripTimerApp.GetProgress(clock, nextAlarm);

            Assert.Equal(expectedProgess, actualProgess.quantized);
        }
        
    }
}
