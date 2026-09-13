namespace AwtrixSharpWeb.Apps.MqttRender
{
    /// <summary>
    /// Detects a second click within a threshold. Uses monotonic <see cref="TimeProvider"/> timestamps,
    /// so wall-clock changes (DST end, NTP steps) can never produce a false double-click.
    /// </summary>
    public class DoubleClickDetector
    {
        private readonly TimeSpan _threshold;
        private readonly TimeProvider _timeProvider;
        private long? _lastClickTimestamp;

        public DoubleClickDetector(double thresholdMilliseconds = 300, TimeProvider? timeProvider = null)
        {
            _threshold = TimeSpan.FromMilliseconds(thresholdMilliseconds);
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <returns>true if this click completes a double-click (the detector then resets)</returns>
        public bool RegisterClick()
        {
            var now = _timeProvider.GetTimestamp();

            if (_lastClickTimestamp is long last && _timeProvider.GetElapsedTime(last, now) <= _threshold)
            {
                _lastClickTimestamp = null;
                return true;
            }

            _lastClickTimestamp = now;
            return false;
        }
    }
}
