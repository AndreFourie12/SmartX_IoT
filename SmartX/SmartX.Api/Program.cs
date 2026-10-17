using System.Globalization;
using System.Text.Json.Serialization;
using SmartX.Api.Commands;
using SmartX.Api.Data;
using SmartX.Api.Endpoints;
using SmartX.Api.Insights;
using SmartX.Api.Services;
using SmartX.Api.Streaming;

// Every string the gateway builds ("Moisture low: 29.6%") is read by clients in any locale, so format
// numbers the same way everywhere rather than with whatever decimal separator the host machine uses.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

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

// In-memory data layer (Part 1)
builder.Services.AddSingleton<SensorRepository>();
builder.Services.AddSingleton<IngestionRateTracker>();
builder.Services.AddSingleton<TelemetryStore>();
builder.Services.AddSingleton<FileEncryptionService>();

// A singleton, so the Telemetry Pulse clean streak survives between requests.
builder.Services.AddSingleton<EngagementService>();

// Real-time processing (Part 2)
builder.Services.AddSingleton<TelemetryIngestionPipeline>();
builder.Services.AddSingleton<LiveDeviceRegistry>();
builder.Services.AddSingleton<TelemetryTimelineStore>();
builder.Services.AddSingleton<AlertTracker>();
builder.Services.AddSingleton<StreamEventLog>();
builder.Services.AddSingleton<ActionRecommendationEngine>();
builder.Services.AddSingleton<CommandHistoryService>();
builder.Services.AddSingleton<CommandStreamService>();
builder.Services.AddSingleton<SimulationControl>();

// The queue consumer and the dropout sweep.
builder.Services.AddHostedService<TelemetryProcessingService>();
builder.Services.AddHostedService<DeviceHealthMonitorService>();

// The simulated device fleet talks to this API over real HTTP, like hardware would.
// Running from Visual Studio's https profile it may only find the dev-certificate address; trusting
// that certificate is scoped to loopback calls the gateway makes to itself.
builder.Services.AddHttpClient(TelemetrySimulationService.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
            errors == System.Net.Security.SslPolicyErrors.None || request.RequestUri?.IsLoopback == true
    });
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
app.MapCommandStreamEndpoints();
app.MapSimulatorEndpoints();

app.MapGet("/", () => Results.Ok(new { service = "Smart-X Data Ingestion Gateway", status = "online" }));

// Give the suggestion engine a few days of mock operator history to learn from.
OperatorHistorySeeder.Seed(app.Services.GetRequiredService<ActionRecommendationEngine>(), DateTimeOffset.UtcNow);

app.Run();
