using System.Diagnostics;
using SmartX.Api.Data;
using SmartX.Shared.Contracts;

namespace SmartX.Api.Endpoints
{
    /// <summary>
    /// 
    /// </summary>
    public static class GatewayEndpoints
    {
        public static void MapGatewayEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/gateway/status", (IngestionRateTracker rate, SensorRepository sensors) =>
                Results.Ok(new GatewayStatusDto(
                    rate.MessagesPerSecond(),
                    DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime(),
                    sensors.GetAll().Count)))
                .WithTags("Gateway");
        }
    }
}

//------------------------------------------- End Of File -------------------------------------------//