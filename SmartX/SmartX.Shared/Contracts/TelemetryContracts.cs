using SmartX.Shared.Models;
using SmartX.Shared.Streaming;

namespace SmartX.Shared.Contracts
{
    /// <summary>
    /// One incoming reading.
    /// Exactly one value field is populated, chosen by DataType.
    /// TimestampUtc is the device's own clock (an ESP32 with NTP stamps its samples); when it is
    /// left out the gateway stamps the packet on arrival instead. Devices flushing a buffer after a
    /// reconnect send old timestamps out of order, which is what the sorted timeline exists to absorb.
    /// Client -> API
    /// </summary>
    public sealed record TelemetryIngestRequest(
        TelemetryDataType DataType,
        float? FloatValue,
        int? IntValue,
        bool? BoolValue,
        string Unit,
        DateTimeOffset? TimestampUtc = null);
    //----------------------------------------------------------//



    /// <summary>
    /// Returned with 202 Accepted the moment a packet is queued, before it is processed.
    /// Lane says whether it joined the FIFO routine queue or jumped to the priority alert lane.
    /// API -> Device
    /// </summary>
    public sealed record TelemetryReceiptDto(
        long Sequence,
        string SensorId,
        IngestLane Lane,
        AlertSeverity? Severity,
        string? Alert,
        int RoutineQueueDepth,
        int AlertQueueDepth);
    //----------------------------------------------------------//



    /// <summary>
    /// One reading from telemetry history
    /// Resolved to a plain string
    /// API -> Client
    /// </summary>
    public sealed record TelemetryDto(
        string SensorId,
        DateTimeOffset TimestampUtc,
        string Value,
        string Unit);
    //----------------------------------------------------------//



    /// <summary>
    /// A batch of raw jagged-array sampling windows to ingest at once.
    /// Client -> API
    /// </summary>
    public sealed record BatchTelemetryIngestRequest(
        TelemetryDataType DataType,
        float[][]? FloatWindows,
        int[][]? IntWindows,
        bool[][]? BoolWindows,
        string Unit);
    //----------------------------------------------------------//



    /// <summary>
    /// Confirms a batch was received and
    /// reports how many readings it produced.
    /// API -> Client
    /// </summary>
    public sealed record BatchTelemetryIngestResponse(
        int PacketsIngested,
        int[] WindowSampleCounts,
        List<TelemetryDto> IngestedReadings)
    {
        public int WindowCount => WindowSampleCounts.Length;
    }
    //----------------------------------------------------------//

}

//------------------------------------------- End Of File -------------------------------------------//
