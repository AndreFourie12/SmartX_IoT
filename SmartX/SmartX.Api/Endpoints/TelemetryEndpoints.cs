using SmartX.Api.Data;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;
using SmartX.Shared.Telemetry;
using System.Globalization;

namespace SmartX.Api.Endpoints
{
    public static class TelemetryEndpoints
    {
        public static void MapTelemetryEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/sensors/{id}/telemetry").WithTags("Telemetry");



            //
            group.MapPost("/", (string id, TelemetryIngestRequest request, SensorRepository sensors, TelemetryStore telemetry) =>
            {
                if (!sensors.TryGet(id, out _))
                    return Results.NotFound($"No sensor registered with id '{id}'");

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
            .Produces(StatusCode.Status404NotFound);


            //
            group.MapPost("/batch", (string id, BatchTelemetryIngestRequest request, SensorRepository sensors, TelemetryStore telemetry) =>
            {
                if (!sensors.TryGet(id, out _))
                    return Results.NotFound($"No sensor registered with id '{id}");

                var packets = BatchTelemetryProcessor.ProcessRawBatches(id, request.Windows, request.Unit);
                telemetry.AddRange(id, packets);

                return Results.Ok(new BatchTelemetryIngestResponse(packets.Count));
            });

            //
            group.MapGet("/", (string id, int? take, SensorRepository sensors, TelemetryStore telemetry) =>
            {
                if (!sensors.TryGet(id, out _))
                    return Results.NotFound($"No sensor registered with id '{id}'.");

                var recent = telemetry.GetRecent(id, take ?? 50).Select(ToDto).ToList();
                return Results.Ok(recent);
            });
        }
        //----------------------------------------------------------//


        /// <summary>
        /// 
        /// </summary>
        private static TelemetryDto ToDto(ITelemetryPacket packet) =>
            new(packet.SensorId, packet.TimestampUtc, Round(packet.ValueAsString), packet.Unit);
        //----------------------------------------------------------//


        /// <summary>
        /// 
        /// </summary>
        private static string Round(string value) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                ? number.ToString("0.##", CultureInfo.InvariantCulture)
                : value;
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
