using System.Threading.Channels;
using SmartX.Shared.Contracts;
using SmartX.Shared.Streaming;

namespace SmartX.Api.Streaming
{
    /// <summary>
    /// The gateway's command and alert history, plus the fan-out behind the live event stream.
    ///
    /// History is a SortedDictionary keyed by (timestamp, event id). Events are published from several
    /// threads - the packet processor, the health monitor, HTTP command requests - so they can reach
    /// the log in a different order from the one they happened in; the sorted key puts them back in
    /// time order regardless, which is the order the history panel reads them in.
    ///
    /// Each connected dashboard gets its own bounded Channel. Publishing writes to every channel and
    /// returns immediately; a slow or stalled client drops its own oldest events rather than blocking
    /// the processor or any other client.
    /// </summary>
    public sealed class StreamEventLog
    {
        private const int Capacity = 500;
        private const int SubscriberBuffer = 256;

        private readonly Lock _gate = new();
        private readonly SortedDictionary<TimelineKey, StreamEventDto> _history = new();
        private readonly List<Channel<StreamEventDto>> _subscribers = [];
        private long _nextId;


        /// <summary>
        /// Adds an event to the history and pushes it to every live subscriber.
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

            Channel<StreamEventDto>[] subscribers;

            lock (_gate)
            {
                _history[new TimelineKey(timestamp, id)] = item;

                while (_history.Count > Capacity)
                    _history.Remove(_history.Keys.First());

                subscribers = _subscribers.ToArray();
            }

            foreach (var channel in subscribers)
                channel.Writer.TryWrite(item);

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


        /// <summary>
        /// Opens a live feed for one SSE client. Dispose the subscription when the client goes away.
        /// </summary>
        public Subscription Subscribe()
        {
            var channel = Channel.CreateBounded<StreamEventDto>(new BoundedChannelOptions(SubscriberBuffer)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });

            lock (_gate)
            {
                _subscribers.Add(channel);
            }

            return new Subscription(this, channel);
        }
        //----------------------------------------------------------//


        public int SubscriberCount
        {
            get { lock (_gate) return _subscribers.Count; }
        }


        private void Unsubscribe(Channel<StreamEventDto> channel)
        {
            lock (_gate)
            {
                _subscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        }
        //----------------------------------------------------------//


        /// <summary>
        /// One client's live feed.
        /// </summary>
        public sealed class Subscription(StreamEventLog owner, Channel<StreamEventDto> channel) : IDisposable
        {
            public ChannelReader<StreamEventDto> Reader => channel.Reader;

            public void Dispose() => owner.Unsubscribe(channel);
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
