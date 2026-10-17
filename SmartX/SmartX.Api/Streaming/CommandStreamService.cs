using SmartX.Api.Commands;
using SmartX.Api.Insights;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;

namespace SmartX.Api.Streaming
{
    /// <summary>
    /// Assembles the Command Stream dashboard's view of the gateway.
    ///
    /// The dashboard refreshes once a second, so everything it shows - queue stats, filtered devices,
    /// alerts, suggestions, the command stack, history, the selected device's timeline - comes back
    /// in one response. One round trip per tick instead of eight keeps the client responsive and the
    /// figures mutually consistent.
    /// </summary>
    public sealed class CommandStreamService(
        LiveDeviceRegistry registry,
        TelemetryIngestionPipeline pipeline,
        AlertTracker alerts,
        TelemetryTimelineStore timeline,
        StreamEventLog events,
        CommandHistoryService commands,
        ActionRecommendationEngine engine)
    {
        private static readonly TimeSpan TrendWindow = TimeSpan.FromMinutes(3);


        public StreamSnapshotDto Snapshot(DeviceQuery query, string? selectedId, int minutes)
        {
            var now = DateTimeOffset.UtcNow;
            var devices = registry.Snapshot();
            var report = Analyse(devices, now);

            TimelineDto? selected = null;
            if (!string.IsNullOrWhiteSpace(selectedId) && registry.TryGetSnapshot(selectedId, out var device))
                selected = timeline.BuildTimeline(device, TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 60)), now);

            return new StreamSnapshotDto(
                now,
                pipeline.Stats(),
                alerts.Summary(NamesOf(devices)),
                registry.Query(query, report.Highlights),
                devices.Count,
                report.Insights,
                engine.Patterns(devices, take: 5),
                report.PredictedSearches,
                report.ActionsObserved,
                commands.History(),
                events.Recent(40),
                selected);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Fits a trend line through each measuring device's recent readings, then runs the engine.
        /// The fits read the SortedDictionary timelines, which hand the points back already in time order.
        /// </summary>
        public InsightReport Analyse(IReadOnlyList<DeviceSnapshot> devices, DateTimeOffset nowUtc)
        {
            var trends = new Dictionary<string, TrendFit>(DeviceIdentifier.Comparer);

            foreach (var device in devices)
            {
                if (device.Category.IsActuator() || device.IsOffline || Shared.Streaming.TelemetryRules.BandFor(device.Category) is null)
                    continue;

                var points = timeline.Range(device.Id, nowUtc - TrendWindow, nowUtc);
                if (TrendAnalysis.Fit(points) is { } fit)
                    trends[device.Id] = fit;
            }

            return engine.Analyse(devices, trends, nowUtc);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Records an operator search for the engine to learn from, and says which devices it matched.
        /// </summary>
        public IReadOnlyList<DeviceSnapshot> LogSearch(string query)
        {
            var resolved = registry.Resolve(query);
            engine.RecordSearch(query, resolved, DateTimeOffset.UtcNow);
            return resolved;
        }
        //----------------------------------------------------------//


        public AlertSummaryDto Alerts() => alerts.Summary(NamesOf(registry.Snapshot()));


        private static Dictionary<string, string> NamesOf(IEnumerable<DeviceSnapshot> devices) =>
            devices.ToDictionary(device => device.Id, device => device.DisplayName, DeviceIdentifier.Comparer);
    }
}

//------------------------------------------- End Of File -------------------------------------------//
