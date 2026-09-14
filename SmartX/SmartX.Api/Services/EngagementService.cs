using SmartX.Api.Data;
using SmartX.Shared.Contracts;

namespace SmartX.Api.Services
{
    /// <summary>
    /// Builds the health snapshot shown by the dashboard's Telemetry Pulse widget.
    /// Works out a score from how many sensors have gone quiet and tracks how many consecutive checks came back clean.
    /// </summary>
    public sealed class EngagementService(SensorRepository sensors, TelemetryStore telemetry)
    {
        private static readonly TimeSpan FreshWindow = TimeSpan.FromSeconds(6);

        private int _cleanStreak;


        /// <summary>
        /// Checks every registered sensor for a recent reading and raises an alert for any that are silent or have never reported.
        /// Turns those alerts into a percentage health score, updates the clean streak, and returns the whole snapshot. 
        /// </summary>
        public EngagementSummaryDto BuildSummary()
        {
            var allSensors = sensors.GetAll();
            var alerts = new List<string>();

            foreach(var sensor in allSensors)
            {
                var latest = telemetry.GetRecent(sensor.Id, take: 1);

                if (latest.Count == 0)
                {
                    alerts.Add($"{sensor.Id}: no telemetry received yet.");
                    continue;
                }

                var silentFor = DateTimeOffset.UtcNow - latest[0].TimestampUtc;

                if(silentFor > FreshWindow)
                {
                    alerts.Add($"{sensor.Id}: silent for {silentFor.TotalSeconds:F0}s - possible dropout.");
                }
            }

            int healthScore = allSensors.Count == 0 ? 100 :
                (int)Math.Round(100.0 * (allSensors.Count - alerts.Count) / allSensors.Count);

            _cleanStreak = alerts.Count == 0 ? _cleanStreak + 1 : 0;


            return new EngagementSummaryDto(healthScore, _cleanStreak, alerts, DateTimeOffset.UtcNow);
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//