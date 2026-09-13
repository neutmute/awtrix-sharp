using AwtrixSharpWeb.Interfaces;

namespace AwtrixSharpWeb.Domain
{
    public class Clock : IClock
    {
        private readonly TimeProvider _timeProvider;

        public Clock(TimeProvider? timeProvider = null)
        {
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public DateTimeOffset Now => _timeProvider.GetLocalNow();

        public TimeProvider TimeProvider => _timeProvider;
    }
}
