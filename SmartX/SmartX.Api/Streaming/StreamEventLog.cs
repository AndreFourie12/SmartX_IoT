using SmartX.Shared.Contracts;
using SmartX.Shared.Streaming;

namespace SmartX.Api.Streaming
{
    /// <summary>
    /// The gateway's command and alert history.
    ///
    /// History is a SortedDictionary keyed by (timestamp, event id). Events are published from several
    /// threads - the packet processor, the health monitor, HTTP command requests - so they can reach
    /// the log in a different order from the one they happened in; the sorted key puts them back in
    /// time order regardless, which is the order the history panel reads them in. The dashboard picks
    /// up new events with each one-second snapshot.
    /// </summary>
    public sealed class StreamEventLog
    {
        private const int Capacity = 500;

        private readonly Lock _gate = new();
        private readonly SortedDictionary<TimelineKey, StreamEventDto> _history = new();
        private long _nextId;


        /// <summary>
        /// Adds an event to the history.
        /// </summary>
        public StreamEventDto Publish(
            StreamEventKind kind,
            AlertSeverity severity,
            string title,
            string detail,
            string? deviceId = null,
            bool viaPriorityLane = false,
            double? latencyMs = null,
            int? bypassed = null)
        {
            var timestamp = DateTimeOffset.UtcNow;
            long id = Interlocked.Increment(ref _nextId);

            var item = new StreamEventDto(id, timestamp, kind, severity, title, detail, deviceId, viaPriorityLane, latencyMs, bypassed);

            lock (_gate)
            {
                _history[new TimelineKey(timestamp, id)] = item;

                while (_history.Count > Capacity)
                    _history.Remove(_history.Keys.First());
            }

            return item;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// The most recent events, newest first.
        /// </summary>
        public List<StreamEventDto> Recent(int take)
        {
            lock (_gate)
            {
                // A SortedDictionary only walks forwards, so take the tail and flip it. Bounded at 500 entries.
                return _history.Values.Skip(Math.Max(0, _history.Count - take)).Reverse().ToList();
            }
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
