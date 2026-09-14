using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartX.Shared.Contracts;
using SmartX.Shared.Validation;

namespace SmartX.Client.Services
{
    /// <summary>
    /// This class acts as a thin wrapper around the smart-X gateway API
    /// Every call is awaited on the UI thread's synchronization context but never blocks it
    /// </summary>
    public sealed class SmartXApiClient
    {
        private readonly HttpClient _http;

        /// <summary>
        /// Serializer settings shared by every call on this client.
        /// Web defaults plus a converter so enums travel as strings rather than numbers.
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };
        //----------------------------------------------------------//



        /// <summary>
        /// Creates the underlying HttpClient and points it at the gateway's base address.
        /// </summary>
        public SmartXApiClient(string baseAddress)
        {
            _http = new HttpClient { BaseAddress = new Uri(baseAddress) };
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Fetches every registered sensor from the gateway.
        /// Returns an empty list rather than null if the response has no body.
        /// </summary>
        public async Task<List<SensorDto>> GetSensorAsync(CancellationToken ct = default) =>
            await _http.GetFromJsonAsync<List<SensorDto>>("/api/sensors", JsonOptions, ct) ?? [];
        //----------------------------------------------------------//



        /// <summary>
        /// Posts a new sensor registration to the gateway.
        /// Throws with the status code and response body if the API rejects it, so the dialog can show the reason.
        /// </summary>
        public async Task<SensorDto?> RegisterSensorAsync(SensorRegistrationRequest request, CancellationToken ct = default)
        {
            var response = await _http.PostAsJsonAsync("/api/sensors", request, JsonOptions, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"Registration failed ({(int)response.StatusCode}): {body}");
            }

            return await response.Content.ReadFromJsonAsync<SensorDto>(JsonOptions, ct);
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Fetches the most recent readings for one sensor, defaulting to the last twenty.
        /// Returns an empty list if nothing comes back.
        /// </summary>
        public async Task<List<TelemetryDto>> GetTelemetryAsync(string sensorId, int take = 20, CancellationToken ct = default) =>
            await _http.GetFromJsonAsync<List<TelemetryDto>>($"/api/sensors/{sensorId}/telemetry?take={take}", JsonOptions, ct) ?? [];
        //----------------------------------------------------------//



        /// <summary>
        /// Posts a jagged batch of sampling windows for one sensor.
        /// Returns the gateway's confirmation, which reports how many readings the batch produced.
        /// </summary>
        public async Task<BatchTelemetryIngestResponse?> IngestBatchAsync(string sensorId, BatchTelemetryIngestRequest request, CancellationToken ct = default)
        {
            var response = await _http.PostAsJsonAsync($"/api/sensors/{sensorId}/telemetry/batch", request, JsonOptions, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<BatchTelemetryIngestResponse>(JsonOptions, ct);
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Fetches the list of files attached to a sensor.
        /// Returns an empty list if that sensor has none.
        /// </summary>
        public async Task<List<AttachmentDto>> GetAttachmentsAsync(string sensorId, CancellationToken ct = default) =>
            await _http.GetFromJsonAsync<List<AttachmentDto>>($"/api/sensors/{sensorId}/attachments", JsonOptions, ct) ?? [];
        //----------------------------------------------------------//



        /// <summary>
        /// Streams a file from disk to the gateway as multipart form data.
        /// Throws with the status code and body if the upload is rejected, otherwise returns the stored attachment's details.
        /// </summary>
        public async Task<AttachmentDto?> UploadAttachmentAsync(string sensorId, string filePath, CancellationToken ct = default)
        {
            await using var fileStream = File.OpenRead(filePath);

            using var content = new MultipartFormDataContent();
            using var streamContent = new StreamContent(fileStream);

            content.Add(streamContent, "file", Path.GetFileName(filePath));

            var response = await _http.PostAsync($"/api/sensors/{sensorId}/attachments", content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"Upload failed ({(int)response.StatusCode}): {body}");
            }

            return await response.Content.ReadFromJsonAsync<AttachmentDto>(JsonOptions, ct);
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Posts the deployment tree built on the client so the recursive validator can run against the real deployment.
        /// Returns the pass or fail result along with every error it found.
        /// </summary>
        public async Task<DeploymentValidatorResult?> ValidateDeploymentAsync(List<DeploymentNode> roots, CancellationToken ct = default)
        {
            var response = await _http.PostAsJsonAsync("/api/deployment/validate", roots, JsonOptions, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<DeploymentValidatorResult>(JsonOptions, ct);
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Fetches throughput, uptime and sensor count for the nav bar's connection strip.
        /// </summary>
        public async Task<GatewayStatusDto?> GetGatewayStatusAsync(CancellationToken ct = default) =>
            await _http.GetFromJsonAsync<GatewayStatusDto>("/api/gateway/status", JsonOptions, ct);
        //----------------------------------------------------------//



        /// <summary>
        /// Fetches the health score, clean streak and active alerts behind the Telemetry Pulse widget.
        /// </summary>
        public async Task<EngagementSummaryDto?> GetEngagementSummaryAsync(CancellationToken ct = default) =>
            await _http.GetFromJsonAsync<EngagementSummaryDto>("/api/engagement/summary", JsonOptions, ct);
        //----------------------------------------------------------//
    }
}


//------------------------------------------- End Of File -------------------------------------------//