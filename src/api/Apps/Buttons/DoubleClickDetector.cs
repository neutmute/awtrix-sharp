namespace AwtrixSharpWeb.Apps.MqttRender
{
    public class DoubleClickDetector
    {
        private readonly TimeSpan threshold;
        private DateTime lastClick;

        public DoubleClickDetector(double thresholdMilliseconds = 300)
        {
            threshold = TimeSpan.FromMilliseconds(thresholdMilliseconds);
            lastClick = DateTime.MinValue;
        }

        /// <returns>true if double</returns>
        public bool RegisterClick()
        {
            var now = DateTime.Now;
            if (now - lastClick <= threshold)
            {
                lastClick = DateTime.MinValue;
                return true; // double click detected
            }

            lastClick = now;
            return false;
        }
    }
}
