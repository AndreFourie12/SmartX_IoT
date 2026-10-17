using System.Diagnostics;
using SmartX.Api.Data;
using SmartX.Shared.Collections;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;
using SmartX.Shared.Streaming;
using SmartX.Shared.Telemetry;

namespace SmartX.Api.Streaming
{
    /// <summary>
    /// The two-lane ingestion queue every telemetry packet passes through.
    ///
    ///   Routine lane - Queue&lt;IngestEnvelope&gt;. Ordinary readings are processed strictly first-in,
    ///                  first-out, so each device's readings are applied in the order they arrived.
    ///
    ///   Alert lane   - PriorityQueue&lt;IngestEnvelope, AlertPriority&gt;. A reading that breaks a
    ///                  threshold (a power spike, a critical moisture drop) is classified the moment
    ///                  it reaches the API and placed here instead. The processor always empties this
    ///                  lane before taking anything from the routine queue, so an alert never waits
    ///                  behind a backlog of routine readings - however long that backlog is.
    ///
    /// The HTTP endpoint only classifies and enqueues, then returns 202 Accepted; the work happens on
    /// TelemetryProcessingService's single consumer loop. Queue and PriorityQueue are not thread-safe,
    /// and many request threads enqueue at once, so both sit behind one short lock. The consumer works
    /// in short cycles; an alert pulses a semaphore that wakes it mid-cycle, so an alert interrupts it
    /// even while it is pacing itself through a routine backlog.
    ///
    /// The routine budget models a resource-constrained edge gateway (think Raspberry Pi class) that can
    /// only process so many packets per second: 20 every 100 ms, 200/s. That is what lets a burst build
    /// a visible backlog.
    /// </summary>
    public sealed class TelemetryIngestionPipeline(IngestionRateTracker rate)
    {
        public const int RoutineBudgetPerCycle = 20;
        public const int CycleMilliseconds = 100;
        public const int RoutineBudgetPerSecond = RoutineBudgetPerCycle * 1000 / CycleMilliseconds;

        private const int WaitSampleSize = 200;

        private readonly Lock _gate = new();

        private readonly Queue<IngestEnvelope> _routine = new();
        private readonly PriorityQueue<IngestEnvelope, AlertPriority> _alerts = new();

        private readonly SemaphoreSlim _alertSignal = new(0, 1);

        private long _sequence;
        private long _routineProcessed;
        private long _alertProcessed;
        private int _lastBypass;

        // Recent queue waits per lane, for the dashboard's average/max wait. Part 1's ring buffer again.
        private readonly TelemetryRingBuffer<double> _routineWaits = new(WaitSampleSize);
        private readonly TelemetryRingBuffer<double> _alertWaits = new(WaitSampleSize);


        /// <summary>
        /// Classifies one packet and places it in the right lane. O(1) for the routine lane,
        /// O(log n) for the alert lane's heap insert.
        /// </summary>
        public EnqueueResult Enqueue(string deviceId, SensorCategory category, ITelemetryPacket packet)
        {
            var assessment = TelemetryRules.Evaluate(category, packet.NumericValue);
            EnqueueResult result;

            lock (_gate)
            {
                result = EnqueueLocked(deviceId, category, packet, assessment);
            }

            if (result.Lane == IngestLane.Alert)
                Pulse(_alertSignal);

            return result;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Queues a whole batch atomically, so no other device's packets land in the middle of it.
        /// </summary>
        public void EnqueueBatch(string deviceId, SensorCategory category, IReadOnlyList<ITelemetryPacket> packets)
        {
            if (packets.Count == 0)
                return;

            // Classify outside the lock - it only reads the packet.
            var assessments = new FaultAssessment?[packets.Count];
            int routineCount = 0;

            for (int i = 0; i < packets.Count; i++)
            {
                assessments[i] = TelemetryRules.Evaluate(category, packets[i].NumericValue);
                if (assessments[i] is null)
                    routineCount++;
            }

            lock (_gate)
            {
                for (int i = 0; i < packets.Count; i++)
                    EnqueueLocked(deviceId, category, packets[i], assessments[i]);
            }

            if (routineCount < packets.Count)
                Pulse(_alertSignal);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Wraps a packet in an envelope and adds it to its lane. The caller must hold the lock.
        /// </summary>
        private EnqueueResult EnqueueLocked(string deviceId, SensorCategory category, ITelemetryPacket packet, FaultAssessment? assessment)
        {
            var lane = assessment is null ? IngestLane.Routine : IngestLane.Alert;
            long sequence = ++_sequence;

            var envelope = new IngestEnvelope
            {
                Sequence = sequence,
                DeviceId = deviceId,
                Category = category,
                Packet = packet,
                Lane = lane,
                Assessment = assessment,
                ArrivedAtUtc = DateTimeOffset.UtcNow,
                EnqueuedTimestamp = Stopwatch.GetTimestamp()
            };

            if (assessment is { } alert)
                _alerts.Enqueue(envelope, new AlertPriority(alert.Severity, sequence));
            else
                _routine.Enqueue(envelope);

            return new EnqueueResult(sequence, lane, assessment, _routine.Count, _alerts.Count);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Takes the next packet to process: always the alert lane's top first, and from the routine
        /// queue only when the caller still has routine budget left this cycle.
        /// </summary>
        public bool TryDequeue(bool allowRoutine, out IngestEnvelope envelope)
        {
            lock (_gate)
            {
                if (_alerts.TryDequeue(out var alert, out _))
                {
                    envelope = alert;
                    return true;
                }

                if (allowRoutine && _routine.TryDequeue(out var routine))
                {
                    envelope = routine;
                    return true;
                }
            }

            envelope = null!;
            return false;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// How many routine packets that arrived before this alert are still waiting - i.e. how many it
        /// overtook. The routine queue is FIFO, so they are all at the front and the walk stops at the
        /// first later arrival.
        /// </summary>
        public int CountRoutineAheadOf(long sequence)
        {
            lock (_gate)
            {
                int ahead = 0;

                foreach (var envelope in _routine)
                {
                    if (envelope.Sequence > sequence)
                        break;

                    ahead++;
                }

                return ahead;
            }
        }
        //----------------------------------------------------------//


        public int RoutineDepth
        {
            get { lock (_gate) return _routine.Count; }
        }


        /// <summary>
        /// Waits out the gap between processing cycles, but wakes immediately if an alert arrives.
        /// </summary>
        public Task<bool> WaitForAlertAsync(TimeSpan timeout, CancellationToken ct) => _alertSignal.WaitAsync(timeout, ct);
        //----------------------------------------------------------//


        /// <summary>
        /// Records a processed packet's queue wait, and for an alert how many routine packets it bypassed.
        /// </summary>
        public void RecordProcessed(IngestEnvelope envelope, double waitedMs, int bypassed)
        {
            lock (_gate)
            {
                if (envelope.Lane == IngestLane.Alert)
                {
                    _alertProcessed++;
                    _alertWaits.Add(waitedMs);
                    _lastBypass = bypassed;
                }
                else
                {
                    _routineProcessed++;
                    _routineWaits.Add(waitedMs);
                }
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Depths, throughput and recent waits for the dashboard header.
        /// </summary>
        public PipelineStatsDto Stats()
        {
            lock (_gate)
            {
                return new PipelineStatsDto(
                    _routine.Count,
                    _alerts.Count,
                    _routineProcessed,
                    _alertProcessed,
                    Average(_routineWaits),
                    Average(_alertWaits),
                    Max(_routineWaits),
                    Max(_alertWaits),
                    rate.MessagesPerSecond(),
                    RoutineBudgetPerSecond,
                    _lastBypass);
            }

            static double Average(TelemetryRingBuffer<double> waits) => waits.Count == 0 ? 0 : Math.Round(waits.Average(), 1);
            static double Max(TelemetryRingBuffer<double> waits) => waits.Count == 0 ? 0 : Math.Round(waits.Max(), 1);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Wakes the consumer if it is not already due to wake. A semaphore capped at one acts as a
        /// latch: however many producers pulse it, the consumer wakes once and then drains everything.
        /// </summary>
        private static void Pulse(SemaphoreSlim signal)
        {
            if (signal.CurrentCount > 0)
                return;

            try
            {
                signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Another producer released it between the check and here - the consumer is already due to wake.
            }
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
