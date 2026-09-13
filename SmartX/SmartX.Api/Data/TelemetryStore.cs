using SmartX.Shared.Collections;
using SmartX.Shared.Telemetry;
using System.Collections.Concurrent;

namespace SmartX.Api.Data
{
    /// <summary>
    /// Per-sensor telemetry history
    /// keyed by sensor id for O(1) access when a packet arrives.
    /// </summary>
    public sealed class TelemetryStore(IngestionRateTracker rateTracker)
    {
        private const int HistoryCapacity = 500;

        private readonly ConcurrentDictionary<string, TelemetryRingBuffer<ITelemetryPacket>> _buffers = new();
        private readonly ConcurrentDictionary<string, object> _locks = new();

        private object LockFor(string sensorId) => _locks.GetOrAdd(sensorId, _ => new object());

        private TelemetryRingBuffer<ITelemetryPacket> BufferFor(string sensorId) =>
            _buffers.GetOrAdd(sensorId, _ => new TelemetryRingBuffer<ITelemetryPacket>(HistoryCapacity));



        /// <summary>
        /// 
        /// </summary>
        public void Add(string sensorId, ITelemetryPacket packet)
        {
            lock (LockFor(sensorId))
            {
                BufferFor(sensorId).Add(packet);
            }

            rateTracker.Record();
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        public void AddRange(string sensorId, List<ITelemetryPacket> packets)
        {
            lock (LockFor(sensorId))
            {
                var buffer = BufferFor(sensorId);

                foreach(var packet in packets)
                {
                    buffer.Add(packet);
                }
            }

            rateTracker.Record(packets.Count);
        }

        /// <summary>
        /// Generic overload for strongly-typed telemetry packets
        /// </summary>
        public void AddRange<T>(string sensorId, List<TelemetryPacket<T>> packets) where T : struct
        {
            lock (LockFor(sensorId))
            {
                var buffer = BufferFor(sensorId);

                foreach(var packet in packets)
                {
                    buffer.Add(packet);
                }
            }

            rateTracker.Record(packets.Count);
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        public List<ITelemetryPacket> GetRecent(string sensorId, int take)
        {
            if (!_buffers.TryGetValue(sensorId, out var buffer))
                return new List<ITelemetryPacket>();

            lock (LockFor(sensorId))
            {
                var recent = new List<ITelemetryPacket>(Math.Min(take, buffer.Count));

                foreach(var packet in buffer.Reverse())
                {
                    if (recent.Count == take)
                        break;

                    recent.Add(packet);
                }

                return recent;
            }
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        public int CountFor(string sensorId) 
            => _buffers.TryGetValue(sensorId, out var buffer) ? buffer.Count : 0;
        //----------------------------------------------------------//

    }
}
//------------------------------------------- End Of File -------------------------------------------//