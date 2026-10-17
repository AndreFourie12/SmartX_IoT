using System.Collections.Concurrent;
using SmartX.Api.Streaming;
using SmartX.Shared.Streaming;

namespace SmartX.Api.Services
{
    /// <summary>
    /// The handle the simulator endpoints use to ask the simulated device fleet to misbehave on demand:
    /// a reconnect storm (every device flushing its buffer at once) or a forced outage on one node.
    /// The fleet picks requests up on its next tick.
    /// </summary>
    public sealed class SimulationControl(StreamEventLog events)
    {
        private int _pendingStormPackets;
        private int _stormRunning;
        private readonly ConcurrentQueue<(string DeviceId, TimeSpan Duration)> _outages = new();

        public bool StormRunning => Volatile.Read(ref _stormRunning) == 1;


        /// <summary>
        /// Queues a storm. Refused while one is already pending or running.
        /// </summary>
        public bool RequestStorm(int packets)
        {
            if (StormRunning || Interlocked.CompareExchange(ref _pendingStormPackets, packets, 0) != 0)
                return false;

            events.Publish(StreamEventKind.LoadTest, AlertSeverity.Info,
                "Reconnect storm requested",
                $"The fleet will flush {packets} buffered readings at once, with one severe power spike hidden halfway through.");

            return true;
        }
        //----------------------------------------------------------//


        public bool TryTakeStorm(out int packets)
        {
            packets = Interlocked.Exchange(ref _pendingStormPackets, 0);

            if (packets <= 0)
                return false;

            Volatile.Write(ref _stormRunning, 1);
            return true;
        }
        //----------------------------------------------------------//


        public void StormFinished(int sent, int rejected, TimeSpan took)
        {
            Volatile.Write(ref _stormRunning, 0);

            events.Publish(StreamEventKind.LoadTest, AlertSeverity.Info,
                "Reconnect storm delivered",
                $"{sent} packets reached the API in {(took.TotalSeconds < 1 ? $"{took.TotalMilliseconds:F0} ms" : $"{took.TotalSeconds:F1} s")}{(rejected > 0 ? $" ({rejected} failed to send)" : string.Empty)}. The routine queue is now draining at the gateway's processing budget.");
        }
        //----------------------------------------------------------//


        public void ForceOutage(string deviceId, TimeSpan duration) => _outages.Enqueue((deviceId, duration));


        public bool TryTakeOutage(out (string DeviceId, TimeSpan Duration) outage) => _outages.TryDequeue(out outage);
    }
}

//------------------------------------------- End Of File -------------------------------------------//
