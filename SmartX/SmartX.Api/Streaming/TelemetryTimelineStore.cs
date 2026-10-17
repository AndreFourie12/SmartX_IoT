using SmartX.Shared.Contracts;
using SmartX.Shared.Models;

namespace SmartX.Api.Streaming
{
    /// <summary>
    /// Per-device historical log, ordered by timestamp, behind the dashboard's timeline graph.
    ///
    /// Each device gets a SortedDictionary&lt;TimelineKey, double&gt; - a red-black tree - so
    /// readings stay in time order however they arrive. That matters because they do not arrive in
    /// order: a device that reconnects flushes its buffered readings with their original timestamps,
    /// and a jagged batch carries a window of past samples. Inserting is O(log n), and reading a window
    /// back is a single in-order walk that stops as soon as it passes the end of the range, so the
    /// client receives points already sorted and draws them as-is.
    ///
    /// Memory is bounded: once a device holds MaxPointsPerDevice readings, the oldest (the tree's
    /// minimum) is evicted for each new one.
    /// </summary>
    public sealed class TelemetryTimelineStore
    {
        public const int MaxPointsPerDevice = 1500;

        private readonly Lock _gate = new();
        private readonly Dictionary<string, SortedDictionary<TimelineKey, double>> _timelines = new(DeviceIdentifier.Comparer);


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
                    timeline = new SortedDictionary<TimelineKey, double>();
                    _timelines[deviceId] = timeline;
                }

                timeline[new TimelineKey(timestampUtc, sequence)] = value;

                // Evict the oldest. First() on a SortedDictionary walks to the leftmost node: O(log n).
                while (timeline.Count > MaxPointsPerDevice)
                    timeline.Remove(timeline.Keys.First());
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Every reading between two instants, oldest first.
        /// The walk is in key order, so it can stop at the first key past the end of the range.
        /// </summary>
        public List<(DateTimeOffset TimestampUtc, double Value)> Range(string deviceId, DateTimeOffset fromUtc, DateTimeOffset toUtc)
        {
            lock (_gate)
            {
                var points = new List<(DateTimeOffset, double)>();

                if (!_timelines.TryGetValue(deviceId, out var timeline))
                    return points;

                foreach (var (key, value) in timeline)
                {
                    if (key.TimestampUtc < fromUtc)
                        continue;

                    if (key.TimestampUtc > toUtc)
                        break;

                    points.Add((key.TimestampUtc, value));
                }

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
