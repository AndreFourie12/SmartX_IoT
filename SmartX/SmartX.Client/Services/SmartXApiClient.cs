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
        /// 
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        public SmartXApiClient(string baseAddress)
        {
            _http = new HttpClient { BaseAddress = new Uri(baseAddress) };
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        public async Task<List<SensorDto>> GetSensorAsync(CancellationToken ct = default) =>
            await _http.GetFromJsonAsync<List<SensorDto>>("/api/sensors", JsonOptions, ct) ?? [];
        //----------------------------------------------------------//



        /// <summary>
        /// 
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
        /// 
        /// </summary>
        public async Task<List<TelemetryDto>> GetTelemetryAsync(string sensorId, int take = 20, CancellationToken ct = default) =>
            await _http.GetFromJsonAsync<List<TelemetryDto>>($"/api/sensors/{sensorId}/telemetry?take={take}", JsonOptions, ct) ?? [];
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        public async Task<BatchTelemetryIngestResponse?> IngestBatchAsync(string sensorId, BatchTelemetryIngestRequest request, CancellationToken ct = default)
        {
            var response = await _http.PostAsJsonAsync($"/api/sensors/{sensorId}/telemetry/batch", request, JsonOptions, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<BatchTelemetryIngestResponse>(JsonOptions, ct);
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        public async Task<List<AttachmentDto>> GetAttachmentsAsync(string sensorId, CancellationToken ct = default) =>
            await _http.GetFromJsonAsync<List<AttachmentDto>>($"/api/sensors/{sensorId}/attachments", JsonOptions, ct) ?? [];
        //----------------------------------------------------------//



        /// <summary>
        /// 
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
        /// 
        /// </summary>
        public async Task<DeploymentValidatorResult?> ValidateDeploymentAsync(List<DeploymentNode> roots, CancellationToken ct = default)
        {
            var response = await _http.PostAsJsonAsync("/api/deployment/validate", roots, JsonOptions, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<DeploymentValidatorResult>(JsonOptions, ct);
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        public async Task<GatewayStatusDto?> GetGatewayStatusAsync(CancellationToken ct = default) =>
            await _http.GetFromJsonAsync<GatewayStatusDto>("/api/gateway/status", JsonOptions, ct);
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        public async Task<EngagementSummaryDto?> GetEngagementSummaryAsync(CancellationToken ct = default) =>
            await _http.GetFromJsonAsync<EngagementSummaryDto>("/api/engagement/summary", JsonOptions, ct);
        //----------------------------------------------------------//
    }
}


//------------------------------------------- End Of File -------------------------------------------//