using AwtrixSharpWeb.Interfaces;

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
}
