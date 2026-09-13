namespace AwtrixSharpWeb.Interfaces
{
    public interface IClock
    {
        DateTimeOffset Now { get; }

        /// <summary>
        /// Time source for timers and delays driven by this clock (cron waits, ActiveTime).
        /// Test clocks that only fake <see cref="Now"/> get the system provider.
        /// </summary>
        TimeProvider TimeProvider => TimeProvider.System;
    }
}
