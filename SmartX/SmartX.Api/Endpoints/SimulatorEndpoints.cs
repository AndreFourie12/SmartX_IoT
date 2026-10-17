using SmartX.Api.Services;
using SmartX.Shared.Models;

namespace SmartX.Api.Endpoints
{
    /// <summary>
    /// Test controls for the simulated device fleet, used by the dashboard's load-test button and the
    /// README's testing walkthrough. They only ask the fleet to behave differently - the resulting
    /// traffic still reaches the API over HTTP like any other device traffic.
    /// </summary>
    public static class SimulatorEndpoints
    {
        public static void MapSimulatorEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/simulator").WithTags("Simulator");


            // Flood the gateway with a reconnect storm (default 1200 packets) to build a routine backlog.
            group.MapPost("/storm", (int? packets, SimulationControl control) =>
            {
                int count = Math.Clamp(packets ?? 1200, 50, 4000);

                return control.RequestStorm(count)
                    ? Results.Accepted(value: new { packets = count, message = "Storm queued - the fleet starts flushing on its next tick." })
                    : Results.Conflict(new { message = "A storm is already running." });
            });


            // Cut power to one simulated node for a while, to watch the disconnect set de-duplicate it.
            group.MapPost("/outage/{id}", (string id, int? seconds, SimulationControl control) =>
            {
                string deviceId = DeviceIdentifier.Normalize(id);

                if (!TelemetrySimulationService.FleetIds.Contains(deviceId, DeviceIdentifier.Comparer))
                    return Results.NotFound($"'{id}' is not one of the simulated devices.");

                int duration = Math.Clamp(seconds ?? 30, 5, 300);
                control.ForceOutage(deviceId, TimeSpan.FromSeconds(duration));

                return Results.Accepted(value: new { deviceId, seconds = duration });
            });


            group.MapGet("/status", (SimulationControl control) =>
                Results.Ok(new { stormRunning = control.StormRunning, devices = TelemetrySimulationService.FleetIds }));
        }
    }
}

//------------------------------------------- End Of File -------------------------------------------//
