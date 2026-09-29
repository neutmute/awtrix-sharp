using AwtrixSharpWeb.Interfaces;

namespace Test.Apps
{
    public class MockClock : IClock
    {
        private DateTimeOffset _currentTime;
        private readonly TimeProvider _timeProvider;

        public MockClock(DateTimeOffset initialTime, TimeProvider? timeProvider = null)
        {
            _currentTime = initialTime;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }
        public DateTimeOffset Now => _currentTime;

        /// <summary>Pass a FakeTimeProvider to drive delays that the code under test takes from IClock.</summary>
        public TimeProvider TimeProvider => _timeProvider;

        public void SetTime(DateTimeOffset newTime)
        {
            _currentTime = newTime;
        }

        public void AdvanceTime(TimeSpan timeSpan)
        {
            _currentTime = _currentTime.Add(timeSpan);
        }
    }   
}
