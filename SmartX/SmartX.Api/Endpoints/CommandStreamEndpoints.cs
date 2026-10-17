using Microsoft.AspNetCore.Mvc;
using SmartX.Api.Commands;
using SmartX.Api.Streaming;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;
using SmartX.Shared.Streaming;

namespace SmartX.Api.Endpoints
{
    /// <summary>
    /// Routes behind the Real-Time Command Stream page: the dashboard snapshot (which carries the
    /// suggestions and the selected device's timeline), device search/filter, queue and alert state,
    /// the history log, and manual override commands with undo.
    /// </summary>
    public static class CommandStreamEndpoints
    {
        public static void MapCommandStreamEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/stream").WithTags("Command Stream");


            // Everything the dashboard draws, in one round trip.
            group.MapGet("/snapshot", (
                [FromQuery] string? q,
                [FromQuery] SensorCategory[]? category,
                [FromQuery] DeviceAlertState[]? alert,
                [FromQuery] string? selected,
                [FromQuery] int? minutes,
                CommandStreamService stream) =>
                    Results.Ok(stream.Snapshot(new DeviceQuery(q, category, alert), selected, minutes ?? 5)));


            // Search / filter on its own: ?q=pump&category=WaterPump&alert=Critical&alert=Offline
            group.MapGet("/devices", (
                [FromQuery] string? q,
                [FromQuery] SensorCategory[]? category,
                [FromQuery] DeviceAlertState[]? alert,
                LiveDeviceRegistry registry,
                CommandStreamService stream) =>
            {
                var report = stream.Analyse(registry.Snapshot(), DateTimeOffset.UtcNow);
                return Results.Ok(registry.Query(new DeviceQuery(q, category, alert), report.Highlights));
            });


            group.MapGet("/pipeline", (TelemetryIngestionPipeline pipeline) => Results.Ok(pipeline.Stats()));

            group.MapGet("/alerts", (CommandStreamService stream) => Results.Ok(stream.Alerts()));

            group.MapGet("/history", (int? take, StreamEventLog events) => Results.Ok(events.Recent(Math.Clamp(take ?? 50, 1, 500))));


            // The device shadow: desired states the (simulated) actuators poll for their orders.
            group.MapGet("/shadow", (LiveDeviceRegistry registry) => Results.Ok(registry.DesiredStates()));


            group.MapPost("/commands", (OverrideCommandRequest request, CommandHistoryService commands) =>
            {
                var result = commands.Execute(request);
                return result.Applied ? Results.Ok(result) : Results.UnprocessableEntity(result);
            });

            group.MapPost("/commands/undo", (CommandHistoryService commands) =>
            {
                var result = commands.Undo();
                return result.Applied ? Results.Ok(result) : Results.Conflict(result);
            });


            // An operator search, logged for pattern learning. Returns which devices it matched.
            group.MapPost("/searches", (SearchLogRequest request, CommandStreamService stream) =>
            {
                if (string.IsNullOrWhiteSpace(request.Query))
                    return Results.BadRequest("Query is required.");

                var matched = stream.LogSearch(request.Query);
                return Results.Ok(matched.Select(device => new { device.Id, device.DisplayName }));
            });
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
