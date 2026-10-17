using SmartX.Api.Data;
using SmartX.Api.Streaming;
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


            // Lists all sensors
            group.MapGet("/", (SensorRepository sensors, TelemetryStore telemetry) =>
            {
                var dtos = sensors.GetAll()
                    .Select(s => ToDto(s, telemetry))
                    .OrderBy(s => s.Id)
                    .ToList();

                return Results.Ok(dtos);
            });


            // Get one specific sensor
            group.MapGet("/{id}", (string id, SensorRepository sensors, TelemetryStore telemetry) =>
            {
                if (!sensors.TryGet(id, out var sensor))
                    return Results.NotFound($"No sensor registered with id '{id}'");

                return Results.Ok(ToDto(sensor, telemetry));

            });


            // Register new sensor. Also files it in the live device dictionary so its packets can be
            // matched in O(1) the moment they start arriving.
            group.MapPost("/", (SensorRegistrationRequest request, SensorRepository sensors, LiveDeviceRegistry registry, TelemetryStore telemetry) =>
            {
                if (string.IsNullOrWhiteSpace(request.id) || string.IsNullOrWhiteSpace(request.Location))
                    return Results.BadRequest("Id and Location are required");

                // The client validates this too, but the API cannot trust that every caller is the client.
                if (!DeviceIdentifier.IsValid(request.id))
                    return Results.BadRequest("Id must be a MAC address (24:6F:28:AA:BB:CC) or a node identifier (ESP32-ENV-04).");

                if (!Enum.IsDefined(request.Category))
                    return Results.BadRequest($"Unknown sensor category '{request.Category}'.");

                string id = DeviceIdentifier.Normalize(request.id);

                if (sensors.Exists(id))
                    return Results.Conflict($"Sensor '{id}' is already registered");

                var sensor = sensors.Register(new SensorDevice
                {
                    Id = id,
                    DisplayName = request.DisplayName?.Trim() ?? string.Empty,
                    Location = request.Location.Trim(),
                    Category = request.Category,
                    RegisteredAtUtc = DateTimeOffset.UtcNow
                });

                registry.Register(sensor);

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
            telemetry.CountFor(sensor.Id),
            string.IsNullOrWhiteSpace(sensor.DisplayName) ? sensor.Id : sensor.DisplayName);
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
