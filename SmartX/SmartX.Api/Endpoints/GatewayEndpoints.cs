using System.Diagnostics;
using SmartX.Api.Data;
using SmartX.Api.Streaming;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;

namespace SmartX.Api.Endpoints
{
    /// <summary>
    /// Exposes the gateway status route used by the connection strip in the nav bar.
    /// Reports throughput, how long the API process has been running, how many sensors are registered,
    /// and how many critical problems are active - which lights the red dot on the Command Stream tab.
    /// </summary>
    public static class GatewayEndpoints
    {
        public static void MapGatewayEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/gateway/status", (IngestionRateTracker rate, SensorRepository sensors, AlertTracker alerts, LiveDeviceRegistry registry) =>
            {
                // Critical faults, plus offline pumps, valves and relays: they can't take commands, which is critical too.
                int critical = alerts.CriticalCount
                    + registry.Snapshot().Count(device => device.IsOffline && device.Category.IsActuator());

                return Results.Ok(new GatewayStatusDto(
                    rate.MessagesPerSecond(),
                    DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime(),
                    sensors.GetAll().Count,
                    critical));
            })
            .WithTags("Gateway");
        }
    }
}

//------------------------------------------- End Of File -------------------------------------------//