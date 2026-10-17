using SmartX.Api.Data;
using SmartX.Api.Streaming;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;
using SmartX.Shared.Telemetry;
using System.Globalization;

namespace SmartX.Api.Endpoints
{
    /// <summary>
    /// Defines every HTTP route for telemetry.
    /// Covers ingesting a single reading, ingesting a jagged batch of sampling windows, and reading back a sensor's recent history.
    ///
    /// Part 2: both ingest routes now hand packets to the TelemetryIngestionPipeline instead of writing
    /// to storage themselves. A single reading is classified, queued (FIFO routine queue, or the priority
    /// alert lane if it breaks a threshold) and acknowledged with 202 Accepted straight away, the way a
    /// real IoT gateway acknowledges a device before it has finished processing.
    /// </summary>
    public static class TelemetryEndpoints
    {
        private static readonly TimeSpan MaxClockSkew = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan MaxBacklogAge = TimeSpan.FromHours(24);


        public static void MapTelemetryEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/sensors/{id}/telemetry").WithTags("Telemetry");

            group.MapPost("/", (string id, TelemetryIngestRequest request, LiveDeviceRegistry registry, TelemetryIngestionPipeline pipeline) =>
            {
                // O(1) dictionary lookup: which device is this, and what category decides its classification.
                if (!registry.TryGetRouting(id, out var deviceId, out var category))
                    return Results.NotFound($"No sensor registered with id '{id}'.");

                if (request.TimestampUtc is { } stamped)
                {
                    var now = DateTimeOffset.UtcNow;

                    if (stamped > now + MaxClockSkew)
                        return Results.BadRequest("TimestampUtc is in the future - check the device's clock.");

                    if (stamped < now - MaxBacklogAge)
                        return Results.BadRequest("TimestampUtc is more than 24 hours old.");
                }

                ITelemetryPacket? packet = request.DataType switch
                {
                    TelemetryDataType.Float when request.FloatValue is { } f => new TelemetryPacket<float>(deviceId, f, request.Unit, request.TimestampUtc),
                    TelemetryDataType.Int when request.IntValue is { } i => new TelemetryPacket<int>(deviceId, i, request.Unit, request.TimestampUtc),
                    TelemetryDataType.Bool when request.BoolValue is { } b => new TelemetryPacket<bool>(deviceId, b, request.Unit, request.TimestampUtc),
                    _ => null
                };

                // Part 1 threw here, which surfaced as a 500. A mismatched payload is the caller's mistake: 400.
                if (packet is null)
                    return Results.BadRequest($"A {request.DataType} reading must populate its matching value field.");

                var result = pipeline.Enqueue(deviceId, category, packet);

                return Results.Accepted($"/api/sensors/{deviceId}/telemetry", new TelemetryReceiptDto(
                    result.Sequence,
                    deviceId,
                    result.Lane,
                    result.Assessment?.Severity,
                    result.Assessment?.Description,
                    result.RoutineDepth,
                    result.AlertDepth));
            })
            .Produces<TelemetryReceiptDto>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);


            group.MapPost("/batch", (string id, BatchTelemetryIngestRequest request, LiveDeviceRegistry registry, TelemetryIngestionPipeline pipeline) =>
            {
                if (!registry.TryGetRouting(id, out var deviceId, out var category))
                    return Results.NotFound($"No sensor registered with id '{id}'.");

                BatchResult? result = request.DataType switch
                {
                    TelemetryDataType.Float when request.FloatWindows is { } windows => Parse(windows),
                    TelemetryDataType.Int when request.IntWindows is { } windows => Parse(windows),
                    TelemetryDataType.Bool when request.BoolWindows is { } windows => Parse(windows),
                    _ => null
                };

                if (result is not { } batch)
                    return Results.BadRequest($"A {request.DataType} batch must populate its matching window array.");

                // The whole batch joins the queue at once and the response goes straight back; the
                // processor works through the packets in its own time, like a single reading.
                pipeline.EnqueueBatch(deviceId, category, batch.Packets);

                return Results.Ok(new BatchTelemetryIngestResponse(
                    batch.Packets.Count,
                    batch.WindowSampleCounts,
                    batch.Packets.Select(ToDto).ToList()));

                BatchResult Parse<T>(T[][] windows) where T : struct
                {
                    var packets = BatchTelemetryProcessor.ProcessRawBatches(deviceId, windows, request.Unit);

                    return new BatchResult(
                        packets.Cast<ITelemetryPacket>().ToList(),
                        windows.Select(window => window.Length).ToArray());
                }
            })
            .Produces<BatchTelemetryIngestResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

            group.MapGet("/", (string id, int? take, SensorRepository sensors, TelemetryStore telemetry) =>
            {
                if (!sensors.TryGet(id, out var sensor))
                    return Results.NotFound($"No sensor registered with id '{id}'.");

                var recent = telemetry.GetRecent(sensor.Id, take ?? 50).Select(ToDto).ToList();
                return Results.Ok(recent);
            });
        }

        /// <summary>
        /// One closed generic's worth of parsed batch, so the three <c>Parse&lt;T&gt;</c> branches can
        /// return the same shape without the value type leaking into the response.
        /// </summary>
        private readonly record struct BatchResult(
            List<ITelemetryPacket> Packets,
            int[] WindowSampleCounts);

        private static TelemetryDto ToDto(ITelemetryPacket packet) =>
            new(packet.SensorId, packet.TimestampUtc, Round(packet.ValueAsString), packet.Unit);

        /// <summary>
        /// Trims a float reading to two decimals for display. Stored telemetry keeps its full
        /// precision - this only affects what the dashboard is sent, so the table doesn't show
        /// values like "62.56176". Ints and bools pass through untouched.
        /// </summary>
        private static string Round(string value) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                ? number.ToString("0.##", CultureInfo.InvariantCulture)
                : value;
    }

}

//------------------------------------------- End Of File -------------------------------------------//
