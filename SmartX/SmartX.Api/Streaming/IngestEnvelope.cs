using System.Diagnostics;
using SmartX.Shared.Models;
using SmartX.Shared.Streaming;
using SmartX.Shared.Telemetry;

namespace SmartX.Api.Streaming
{
    /// <summary>
    /// One packet waiting in an ingestion queue, carrying what the processor needs: the typed packet,
    /// the classification made on arrival, and the moment it was queued so its wait can be measured.
    /// </summary>
    public sealed class IngestEnvelope
    {
        public required long Sequence { get; init; }
        public required string DeviceId { get; init; }
        public required SensorCategory Category { get; init; }
        public required ITelemetryPacket Packet { get; init; }
        public required IngestLane Lane { get; init; }
        public FaultAssessment? Assessment { get; init; }
        public required DateTimeOffset ArrivedAtUtc { get; init; }
        public required long EnqueuedTimestamp { get; init; }

        public double WaitedMilliseconds(long nowTimestamp) =>
            Stopwatch.GetElapsedTime(EnqueuedTimestamp, nowTimestamp).TotalMilliseconds;
    }
    //----------------------------------------------------------//


    /// <summary>
    /// Ordering for the alert lane. .NET's PriorityQueue is a min-heap - the smallest priority comes
    /// out first - so Critical has to compare as smaller than Warning. Within a severity the lower
    /// sequence number (the earlier arrival) wins, which keeps the lane fair: a stream of new critical
    /// alerts can never starve an older one.
    /// </summary>
    public readonly record struct AlertPriority(AlertSeverity Severity, long Sequence) : IComparable<AlertPriority>
    {
        public int CompareTo(AlertPriority other)
        {
            int bySeverity = other.Severity.CompareTo(Severity);
            return bySeverity != 0 ? bySeverity : Sequence.CompareTo(other.Sequence);
        }
    }
    //----------------------------------------------------------//


    /// <summary>
    /// Where a packet offered to the pipeline was queued.
    /// </summary>
    public readonly record struct EnqueueResult(
        long Sequence,
        IngestLane Lane,
        FaultAssessment? Assessment,
        int RoutineDepth,
        int AlertDepth);
    //----------------------------------------------------------//
}

//------------------------------------------- End Of File -------------------------------------------//
