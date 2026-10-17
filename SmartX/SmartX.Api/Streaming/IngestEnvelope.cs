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
        public BatchCompletion? Batch { get; init; }

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
    /// Lets a batch request await the processing of its own packets without holding a thread.
    /// Each packet signals when processed; the last one completes the task. Counting down, rather
    /// than waiting for the final packet, matters because a batch's critical readings jump into the
    /// alert lane and can finish before the routine ones queued ahead of them.
    /// </summary>
    public sealed class BatchCompletion(int packetCount)
    {
        private int _remaining = packetCount;
        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _done.Task;

        public void Signal()
        {
            if (Interlocked.Decrement(ref _remaining) == 0)
                _done.TrySetResult();
        }
    }
    //----------------------------------------------------------//


    /// <summary>
    /// Tunables for the pipeline, bound from the "Pipeline" section of appsettings.json.
    /// The routine budget models a resource-constrained edge gateway (think Raspberry Pi class) that can
    /// only process so many packets per second; it is what lets a burst build a visible backlog.
    /// </summary>
    public sealed class PipelineOptions
    {
        public int RoutineBudgetPerCycle { get; set; } = 20;
        public int CycleMilliseconds { get; set; } = 100;
        public int RoutineCapacity { get; set; } = 5000;

        public int RoutineBudgetPerSecond => RoutineBudgetPerCycle * 1000 / Math.Max(1, CycleMilliseconds);
    }
    //----------------------------------------------------------//


    /// <summary>
    /// What happened to a packet offered to the pipeline.
    /// </summary>
    public readonly record struct EnqueueResult(
        bool Accepted,
        long Sequence,
        IngestLane Lane,
        FaultAssessment? Assessment,
        int RoutineDepth,
        int AlertDepth);
    //----------------------------------------------------------//
}

//------------------------------------------- End Of File -------------------------------------------//
