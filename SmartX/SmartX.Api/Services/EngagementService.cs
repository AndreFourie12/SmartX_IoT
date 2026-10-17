using SmartX.Api.Streaming;
using SmartX.Shared.Contracts;

namespace SmartX.Api.Services
{
    /// <summary>
    /// Builds the health snapshot shown by the dashboard's Telemetry Pulse widget.
    /// Works out a score from how many sensors have gone quiet and tracks how many consecutive checks came back clean.
    ///
    /// Part 2 changes: this is now a singleton - as a scoped service it was rebuilt for every request,
    /// so the clean streak reset each poll and could never climb past 1. It also reads "last heard from"
    /// out of the live device dictionary (arrival time) instead of the newest packet's own timestamp,
    /// so a device flushing old buffered readings is not mistaken for one that has gone silent.
    /// </summary>
    public sealed class EngagementService(LiveDeviceRegistry registry)
    {
        private static readonly TimeSpan FreshWindow = TimeSpan.FromSeconds(6);

        private int _cleanStreak;


        /// <summary>
        /// Checks every registered sensor for a recent reading and raises an alert for any that are silent or have never reported.
        /// Turns those alerts into a percentage health score, updates the clean streak, and returns the whole snapshot.
        /// </summary>
        public EngagementSummaryDto BuildSummary()
        {
            var allSensors = registry.Snapshot();
            var alerts = new List<string>();
            var now = DateTimeOffset.UtcNow;

            foreach(var sensor in allSensors.OrderBy(s => s.Id, StringComparer.Ordinal))
            {
                if (sensor.LastSeenUtc is not { } lastSeen)
                {
                    alerts.Add($"{sensor.Id}: no telemetry received yet.");
                    continue;
                }

                var silentFor = now - lastSeen;

                if(silentFor > FreshWindow)
                {
                    alerts.Add($"{sensor.Id}: silent for {silentFor.TotalSeconds:F0}s - possible dropout.");
                }
            }

            int healthScore = allSensors.Count == 0 ? 100 :
                (int)Math.Round(100.0 * (allSensors.Count - alerts.Count) / allSensors.Count);

            int streak = 0;

            if (alerts.Count == 0)
                streak = Interlocked.Increment(ref _cleanStreak);
            else
                Interlocked.Exchange(ref _cleanStreak, 0);

            return new EngagementSummaryDto(healthScore, streak, alerts, now);
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
