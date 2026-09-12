using SmartX.Api.Data;
using SmartX.Shared.Contracts;

namespace SmartX.Api.Services
{
    /// <summary>
    /// 
    /// </summary>
    public sealed class EngagementService(SensorRepository sensors, TelemetryStore telemetry)
    {
        private static readonly TimeSpan FreshWindow = TimeSpan.FromSeconds(6);

        private int _cleanStreak;


        /// <summary>
        /// 
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