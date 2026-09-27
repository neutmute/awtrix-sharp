namespace AwtrixSharpWeb.Services
{
    /// <summary>One request handed to a transport by <see cref="AwtrixService"/>. AppName is null for device-level operations.</summary>
    public sealed record PublishRecord(DateTimeOffset At, string BaseTopic, string Operation, string? AppName, AwtrixRequest Request, bool Delivered);

    /// <summary>
    /// In-memory trace of recent sends for the test UI: a bounded newest-first list plus the last
    /// app-level record per (device, app). Thread-safe; a throwing Changed subscriber is swallowed.
    /// </summary>
    public class PublishTrace
    {
        public const int DefaultCapacity = 50;

        private readonly object _lock = new();
        private readonly LinkedList<PublishRecord> _recent = new();
        private readonly Dictionary<(string, string), PublishRecord> _lastForApp = new();
        private readonly int _capacity;

        public PublishTrace(int capacity = DefaultCapacity)
        {
            _capacity = Math.Max(1, capacity);
        }

        public event Action? Changed;

        public void Record(PublishRecord record)
        {
            lock (_lock)
            {
                _recent.AddFirst(record);
                while (_recent.Count > _capacity)
                {
                    _recent.RemoveLast();
                }

                if (record.AppName != null)
                {
                    _lastForApp[(record.BaseTopic, record.AppName)] = record;
                }
            }

            try
            {
                Changed?.Invoke();
            }
            catch
            {
                // UI subscribers must never break publishing
            }
        }

        public IReadOnlyList<PublishRecord> Recent()
        {
            lock (_lock)
            {
                return _recent.ToArray();
            }
        }

        public PublishRecord? LastForApp(string baseTopic, string appName)
        {
            lock (_lock)
            {
                return _lastForApp.TryGetValue((baseTopic, appName), out var record) ? record : null;
            }
        }
    }
}
