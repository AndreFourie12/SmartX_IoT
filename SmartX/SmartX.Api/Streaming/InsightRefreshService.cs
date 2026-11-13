using SmartX.Api.Insights;

namespace SmartX.Api.Streaming
{
    /// <summary>
    /// Keeps the prediction engine's analysis fresh in the background.
    ///
    /// Every second this fits the trend lines, runs the recommendation engine over the whole fleet and
    /// publishes the finished report on the stream service. The dashboard's polls then only read that
    /// report instead of computing it themselves, so the analysis happens ahead of the question rather
    /// than on it. A failed pass is logged and the loop carries on; the last good report stays in place.
    /// </summary>
    public sealed class InsightRefreshService(
        CommandStreamService stream,
        LiveDeviceRegistry registry,
        ILogger<InsightRefreshService> logger) : BackgroundService
    {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(RefreshInterval);

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    try
                    {
                        stream.PublishReport(stream.Analyse(registry.Snapshot(), DateTimeOffset.UtcNow));
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Insight refresh failed.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
