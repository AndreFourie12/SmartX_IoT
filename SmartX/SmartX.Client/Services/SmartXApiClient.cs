using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;
using SmartX.Shared.Streaming;
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
        public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };
        //----------------------------------------------------------//



        /// <summary>
        /// Creates the underlying HttpClient and points it at the gateway's base address.
        /// A short timeout, so a gateway that has gone away fails a poll quickly instead of stalling it.
        /// </summary>
        public SmartXApiClient(string baseAddress)
        {
            _http = new HttpClient { BaseAddress = new Uri(baseAddress), Timeout = TimeSpan.FromSeconds(20) };
        }
        //----------------------------------------------------------//


        public Uri BaseAddress => _http.BaseAddress!;


        /// <summary>
        /// Device ids can be MAC addresses, so they are escaped before going into a URL path.
        /// </summary>
        private static string Id(string sensorId) => Uri.EscapeDataString(sensorId);



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
            await _http.GetFromJsonAsync<List<TelemetryDto>>($"/api/sensors/{Id(sensorId)}/telemetry?take={take}", JsonOptions, ct) ?? [];
        //----------------------------------------------------------//



        /// <summary>
        /// Posts a jagged batch of sampling windows for one sensor.
        /// Returns the gateway's confirmation, which reports how many readings the batch produced.
        /// </summary>
        public async Task<BatchTelemetryIngestResponse?> IngestBatchAsync(string sensorId, BatchTelemetryIngestRequest request, CancellationToken ct = default)
        {
            var response = await _http.PostAsJsonAsync($"/api/sensors/{Id(sensorId)}/telemetry/batch", request, JsonOptions, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<BatchTelemetryIngestResponse>(JsonOptions, ct);
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Posts one reading exactly as a device would. The receipt says which queue it was routed into.
        /// </summary>
        public async Task<TelemetryReceiptDto?> IngestTelemetryAsync(string sensorId, TelemetryIngestRequest request, CancellationToken ct = default)
        {
            var response = await _http.PostAsJsonAsync($"/api/sensors/{Id(sensorId)}/telemetry", request, JsonOptions, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"Ingest failed ({(int)response.StatusCode}): {body}");
            }

            return await response.Content.ReadFromJsonAsync<TelemetryReceiptDto>(JsonOptions, ct);
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Fetches the list of files attached to a sensor.
        /// Returns an empty list if that sensor has none.
        /// </summary>
        public async Task<List<AttachmentDto>> GetAttachmentsAsync(string sensorId, CancellationToken ct = default) =>
            await _http.GetFromJsonAsync<List<AttachmentDto>>($"/api/sensors/{Id(sensorId)}/attachments", JsonOptions, ct) ?? [];
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

            var response = await _http.PostAsync($"/api/sensors/{Id(sensorId)}/attachments", content, ct);

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



        // ============================ Part 2: Real-Time Command Stream ============================ //

        /// <summary>
        /// Everything the Command Stream page draws, in one round trip. The search text and filters
        /// travel as query parameters, so the gateway does the filtering against its index sets.
        /// </summary>
        public async Task<StreamSnapshotDto?> GetStreamSnapshotAsync(
            string? search,
            IEnumerable<SensorCategory> categories,
            IEnumerable<DeviceAlertState> alertStates,
            string? selectedId,
            int minutes,
            CancellationToken ct = default)
        {
            var query = new StringBuilder($"/api/stream/snapshot?minutes={minutes}");

            if (!string.IsNullOrWhiteSpace(search))
                query.Append("&q=").Append(Uri.EscapeDataString(search.Trim()));

            foreach (var category in categories)
                query.Append("&category=").Append(category);

            foreach (var state in alertStates)
                query.Append("&alert=").Append(state);

            if (!string.IsNullOrWhiteSpace(selectedId))
                query.Append("&selected=").Append(Uri.EscapeDataString(selectedId));

            return await _http.GetFromJsonAsync<StreamSnapshotDto>(query.ToString(), JsonOptions, ct);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Issues a manual override. A refusal (nothing to change, unknown device) comes back as a result
        /// with Applied = false rather than an exception, so the page can show the gateway's reason.
        /// </summary>
        public Task<CommandResultDto?> IssueCommandAsync(OverrideCommandRequest request, CancellationToken ct = default) =>
            ReadCommandResultAsync(_http.PostAsJsonAsync("/api/stream/commands", request, JsonOptions, ct), ct);

        public Task<CommandResultDto?> UndoAsync(CancellationToken ct = default) =>
            ReadCommandResultAsync(_http.PostAsync("/api/stream/commands/undo", null, ct), ct);

        private static async Task<CommandResultDto?> ReadCommandResultAsync(Task<HttpResponseMessage> send, CancellationToken ct)
        {
            using var response = await send;
            return await response.Content.ReadFromJsonAsync<CommandResultDto>(JsonOptions, ct);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Logs an operator search so the suggestion engine can learn from it.
        /// </summary>
        public async Task LogSearchAsync(string query, CancellationToken ct = default)
        {
            using var response = await _http.PostAsJsonAsync("/api/stream/searches", new SearchLogRequest(query), JsonOptions, ct);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Asks the simulated fleet for a reconnect storm. False if one is already running.
        /// </summary>
        public async Task<bool> TriggerStormAsync(int packets = 1200, CancellationToken ct = default)
        {
            using var response = await _http.PostAsync($"/api/simulator/storm?packets={packets}", null, ct);
            return response.IsSuccessStatusCode;
        }
        //----------------------------------------------------------//
    }
}


//------------------------------------------- End Of File -------------------------------------------//
