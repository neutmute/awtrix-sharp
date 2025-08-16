using AwtrixSharpWeb.Apps;
using AwtrixSharpWeb.Domain;
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
        public void Fact()
        {
            _clock = new Clock();

            // Arrange
            var sut = GetSystemUnderTest();


            // Act
            sut.Initialize();
            
        }
    }
}
