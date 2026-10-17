using SmartX.Api.Insights;
using SmartX.Shared.Models;
using SmartX.Shared.Streaming;

namespace SmartX.Api.Streaming
{
    /// <summary>
    /// Sweeps the fleet every two seconds for nodes that have gone quiet.
    ///
    /// A node that dies stays silent on every sweep after, so a naive monitor would raise "offline"
    /// fifteen times in thirty seconds. Instead each sweep hands the whole silent list to the alert
    /// tracker's disconnected-node HashSet, which returns only the nodes that weren't already in it.
    /// One alert per outage; the repeats are counted as suppressed. The node leaves the set the
    /// moment its next packet is processed, which is what re-arms the alert for a later outage.
    /// </summary>
    public sealed class DeviceHealthMonitorService(
        LiveDeviceRegistry registry,
        AlertTracker alerts,
        StreamEventLog events,
        ActionRecommendationEngine engine,
        ILogger<DeviceHealthMonitorService> logger) : BackgroundService
    {
        public static readonly TimeSpan OfflineAfter = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(2);


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(SweepInterval);

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    try
                    {
                        Sweep(DateTimeOffset.UtcNow);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Device health sweep failed.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
        }
        //----------------------------------------------------------//


        private void Sweep(DateTimeOffset nowUtc)
        {
            var silent = registry.SilentDevices(nowUtc, OfflineAfter);

            foreach (var id in alerts.RecordSilent(silent))
            {
                string name = registry.SetOffline(id, true);
                bool isActuator = registry.TryGetSnapshot(id, out var device) && device.Category.IsActuator();
                var severity = isActuator ? AlertSeverity.Critical : AlertSeverity.Warning;

                engine.OnConditionRaised(new ConditionKey(id, FaultKind.Offline), severity, "Node offline", nowUtc);

                events.Publish(StreamEventKind.DeviceOffline, severity,
                    $"{name} went offline",
                    $"No telemetry for {OfflineAfter.TotalSeconds:F0}s. Repeat dropout alerts for it are suppressed until it reconnects.",
                    id);
            }
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
