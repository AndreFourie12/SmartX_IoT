using SmartX.Api.Data;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;

namespace SmartX.Api.Endpoints
{
    /// <summary>
    /// Defines every HTTP route related to sensors
    /// List all sensors, get one sensors, register a new Sensor
    /// </summary>
    public static class SensorEndpoints
    {
        public static void MapSensorEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/sensors").WithTags("Sensors");

            group.MapGet("/", (SensorRepository sensors, TelemetryStore telemetry) =>
            {
                var dtos = sensors.GetAll()
                    .Select(s => ToDto(s, telemetry))
                    .OrderBy(s => s.Id)
                    .ToList();

                return Results.Ok(dtos);
            });


            
            group.MapGet("/{id}", (string id, SensorRepository sensors, TelemetryStore telemetry) =>
            {
                if (!sensors.TryGet(id, out var sensor))
                    return Results.NotFound($"No sensor registered with id '{id}'");

                return Results.Ok(ToDto(sensor, telemetry));

            });

            group.MapPost("/", (SensorRegistrationRequest request, SensorRepository sensors, TelemetryStore telemetry) =>
            {
                if (string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Location))
                    return Results.BadRequest("Id and Location are required");

                if (sensors.Exists(request.Id))
                    return Results.Conflict($"Sensor '{request.Id}' is already registered");

                var sensor = sensors.Register(new SensorDevice
                {
                    Id = request.id,
                    Location = request.Location,
                    Category = request.Category,
                    RegisteredAtUtc = DateTimeOffset.UtcNow
                });

                return Results.Created($"/api/sensors/{sensor.Id}", ToDto(sensor, telemetry));
            });
        }
        //----------------------------------------------------------//


        /// <summary>
        /// SensorDevice -> Sensor Dto conversion
        /// Reused by all 3 routes
        /// </summary>
        private static SensorDto ToDto(SensorDevice sensor, TelemetryStore telemetry) => new(
            sensor.Id,
            sensor.Location,
            sensor.Category,
            sensor.RegisteredAtUtc,
            sensor.Attachments.Count,
            telemetry.CountFor(sensor.Id));
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
