using SmartX.Api.Data;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;
using SmartX.Shared.Telemetry;
using System.Globalization;

namespace SmartX.Api.Endpoints
{
    /// <summary>
    /// Defines every HTTP route for telemetry.
    /// Covers ingesting a single reading, ingesting a jagged batch of sampling windows, and reading back a sensor's recent history.
    /// </summary>
    public static class TelemetryEndpoints
    {
        public static void MapTelemetryEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/sensors/{id}/telemetry").WithTags("Telemetry");

            group.MapPost("/", (string id, TelemetryIngestRequest request, SensorRepository sensors, TelemetryStore telemetry) =>
            {
                if (!sensors.TryGet(id, out _))
                    return Results.NotFound($"No sensor registered with id '{id}'.");

                ITelemetryPacket packet = request.DataType switch
                {
                    TelemetryDataType.Float when request.FloatValue is { } f => new TelemetryPacket<float>(id, f, request.Unit),
                    TelemetryDataType.Int when request.IntValue is { } i => new TelemetryPacket<int>(id, i, request.Unit),
                    TelemetryDataType.Bool when request.BoolValue is { } b => new TelemetryPacket<bool>(id, b, request.Unit),
                    _ => throw new InvalidOperationException()
                };

                telemetry.Add(id, packet);
                return Results.Ok(ToDto(packet));
            })
            .Produces<TelemetryDto>()
            .Produces(StatusCodes.Status404NotFound);

            group.MapPost("/batch", (string id, BatchTelemetryIngestRequest request, SensorRepository sensors, TelemetryStore telemetry) =>
            {
                if (!sensors.TryGet(id, out _))
                    return Results.NotFound($"No sensor registered with id '{id}'.");

                BatchResult? result = request.DataType switch
                {
                    TelemetryDataType.Float when request.FloatWindows is { } windows => Ingest(windows),
                    TelemetryDataType.Int when request.IntWindows is { } windows => Ingest(windows),
                    TelemetryDataType.Bool when request.BoolWindows is { } windows => Ingest(windows),
                    _ => null
                };

                if (result is not { } batch)
                    return Results.BadRequest($"A {request.DataType} batch must populate its matching window array.");

                return Results.Ok(new BatchTelemetryIngestResponse(
                    batch.PacketsIngested,
                    batch.WindowSampleCounts,
                    batch.IngestedReadings));

                BatchResult Ingest<T>(T[][] windows) where T : struct
                {
                    var packets = BatchTelemetryProcessor.ProcessRawBatches(id, windows, request.Unit);
                    telemetry.AddRange(id, packets);

                    return new BatchResult(
                        packets.Count,
                        windows.Select(window => window.Length).ToArray(),
                        packets.Select(ToDto).ToList());
                }
            })
            .Produces<BatchTelemetryIngestResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

            group.MapGet("/", (string id, int? take, SensorRepository sensors, TelemetryStore telemetry) =>
            {
                if (!sensors.TryGet(id, out _))
                    return Results.NotFound($"No sensor registered with id '{id}'.");

                var recent = telemetry.GetRecent(id, take ?? 50).Select(ToDto).ToList();
                return Results.Ok(recent);
            });
        }

        /// <summary>
        /// One closed generic's worth of batch work, so the three <c>Ingest&lt;T&gt;</c> branches can
        /// return the same shape without the value type leaking into the response.
        /// </summary>
        private readonly record struct BatchResult(
            int PacketsIngested,
            int[] WindowSampleCounts,
            List<TelemetryDto> IngestedReadings);

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
