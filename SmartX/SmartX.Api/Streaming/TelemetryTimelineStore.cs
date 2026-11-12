using SmartX.Shared.Collections;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;

namespace SmartX.Api.Streaming
{
    /// <summary>
    /// Per-device historical log, ordered by timestamp, behind the dashboard's timeline graph.
    ///
    /// Each device gets an AvlTree&lt;TimelineKey, double&gt; - a hand-written self-balancing tree - so
    /// readings stay in time order however they arrive. That matters because they do not arrive in
    /// order: a device that reconnects flushes its buffered readings with their original timestamps,
    /// and a jagged batch carries a window of past samples. Inserting is O(log n), and reading a window
    /// back goes straight to the start of the range and stops at its end (O(log n + k)), so the
    /// client receives points already sorted and draws them as-is.
    ///
    /// Memory is bounded: once a device holds MaxPointsPerDevice readings, the oldest (the tree's
    /// minimum) is evicted for each new one.
    /// </summary>
    public sealed class TelemetryTimelineStore
    {
        // About 5.5 hours at one reading per four seconds, so a two-hour history query is realistic.
        public const int MaxPointsPerDevice = 5000;

        private readonly Lock _gate = new();
        private readonly Dictionary<string, AvlTree<TimelineKey, double>> _timelines = new(DeviceIdentifier.Comparer);


        /// <summary>
        /// Inserts one reading at its place in time.
        /// </summary>
        public void Append(string deviceId, DateTimeOffset timestampUtc, long sequence, double value)
        {
            if (double.IsNaN(value))
                return;

            lock (_gate)
            {
                if (!_timelines.TryGetValue(deviceId, out var timeline))
                {
                    timeline = new AvlTree<TimelineKey, double>();
                    _timelines[deviceId] = timeline;
                }

                timeline.Add(new TimelineKey(timestampUtc, sequence), value);

                // Evict the oldest: the tree's smallest key, found and removed in O(log n).
                while (timeline.Count > MaxPointsPerDevice)
                    timeline.RemoveMin();
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Every reading between two instants, oldest first.
        /// The tree skips whole subtrees outside the range, so a late window is as cheap as an early one.
        /// The bounds use the smallest and largest possible sequence so that a reading stamped exactly at
        /// 'from' or 'to' is still included.
        /// </summary>
        public List<(DateTimeOffset TimestampUtc, double Value)> Range(string deviceId, DateTimeOffset fromUtc, DateTimeOffset toUtc)
        {
            lock (_gate)
            {
                var points = new List<(DateTimeOffset, double)>();

                if (!_timelines.TryGetValue(deviceId, out var timeline))
                    return points;

                var found = new List<KeyValuePair<TimelineKey, double>>();
                timeline.Range(new TimelineKey(fromUtc, long.MinValue), new TimelineKey(toUtc, long.MaxValue), found);

                foreach (var pair in found)
                    points.Add((pair.Key.TimestampUtc, pair.Value));

                return points;
            }
        }
        //----------------------------------------------------------//


        public int CountFor(string deviceId)
        {
            lock (_gate)
            {
                return _timelines.TryGetValue(deviceId, out var timeline) ? timeline.Count : 0;
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Builds the timeline DTO for one device over the last few minutes.
        /// </summary>
        public TimelineDto BuildTimeline(DeviceSnapshot device, TimeSpan window, DateTimeOffset nowUtc)
        {
            var from = nowUtc - window;
            var points = Range(device.Id, from, nowUtc)
                .Select(point => new TimelinePointDto(
                    point.TimestampUtc,
                    point.Value,
                    LiveDeviceRegistry.FormatValue(device.Category, point.Value) ?? string.Empty))
                .ToList();

            return new TimelineDto(
                device.Id,
                device.DisplayName,
                device.Category,
                device.Category.Unit(),
                from,
                nowUtc,
                points,
                Shared.Streaming.TelemetryRules.BandFor(device.Category),
                CountFor(device.Id));
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
