using SmartX.Shared.Models;
using System;
using System.Collections.Generic;
using System.Text;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace SmartX.Shared.Contracts
{
    /// <summary>
    /// One incoming reading. 
    /// Exactly one value field is populated, chosen by DataType.
    /// Client -> API
    /// </summary>
    public sealed record TelemetryIngestRequest(
        TelemetryDataType DataType,
        float? FloatValue,
        int? IntValue,
        bool? BoolValue,
        string Unit);
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