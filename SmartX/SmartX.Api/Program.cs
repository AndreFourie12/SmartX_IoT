using System.Text.Json.Serialization;
using SmartX.Api.Data;
using SmartX.Api.Endpoints;
using SmartX.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

// In-memory data layer 
builder.Services.AddSingleton<SensorRepository>();
builder.Services.AddSingleton<IngestionRateTracker>();
builder.Services.AddSingleton<TelemetryStore>();
builder.Services.AddSingleton<FileEncryptionService>();
builder.Services.AddScoped<EngagementService>();

// Seeds mock sensors/telemetry at startup and keeps generating live readings.
builder.Services.AddHostedService<TelemetrySimulationService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

app.MapSensorEndpoints();
app.MapTelemetryEndpoints();
app.MapAttachmentEndpoints();
app.MapDeploymentEndpoints();
app.MapEngagementEndpoints();
app.MapGatewayEndpoints();

app.MapGet("/", () => Results.Ok(new { service = "Smart-X Data Ingestion Gateway", status = "online" }));

app.Run();
