using System.Diagnostics;
using SmartX.Api.Data;
using SmartX.Api.Insights;
using SmartX.Shared.Models;
using SmartX.Shared.Streaming;

namespace SmartX.Api.Streaming
{
    /// <summary>
    /// The single consumer behind the ingestion pipeline.
    ///
    /// Each cycle it drains the alert lane completely, then takes up to RoutineBudgetPerCycle packets
    /// from the routine FIFO queue. Because TryDequeue checks the alert lane before every single
    /// routine packet, an alert arriving mid-cycle is the very next thing processed. Between batches
    /// the loop pauses until the next cycle, but that pause waits on the alert signal, so even a
    /// gateway deep in a routine backlog reacts to an alert within milliseconds.
    ///
    /// One consumer means each device's routine readings are applied in arrival order with no locking
    /// between packets of the same device.
    ///
    /// Processing a packet: store it in Part 1's ring buffer, insert it into the sorted timeline,
    /// update the cached device state (O(1) dictionary lookup), run it past the active-fault HashSet,
    /// and publish whatever genuinely changed.
    /// </summary>
    public sealed class TelemetryProcessingService(
        TelemetryIngestionPipeline pipeline,
        TelemetryStore store,
        TelemetryTimelineStore timeline,
        LiveDeviceRegistry registry,
        AlertTracker alerts,
        StreamEventLog events,
        ActionRecommendationEngine engine,
        ILogger<TelemetryProcessingService> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var cycle = TimeSpan.FromMilliseconds(TelemetryIngestionPipeline.CycleMilliseconds);
            const int budget = TelemetryIngestionPipeline.RoutineBudgetPerCycle;

            long cycleStart = Stopwatch.GetTimestamp();
            int routineThisCycle = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                if (Stopwatch.GetElapsedTime(cycleStart) >= cycle)
                {
                    cycleStart = Stopwatch.GetTimestamp();
                    routineThisCycle = 0;
                }

                // Alerts always come out first; routine packets only while this cycle has budget left.
                while (pipeline.TryDequeue(allowRoutine: routineThisCycle < budget, out var envelope))
                {
                    try
                    {
                        Process(envelope);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to process packet {Sequence} from {Device}.", envelope.Sequence, envelope.DeviceId);
                    }

                    if (envelope.Lane == IngestLane.Routine)
                        routineThisCycle++;
                }

                try
                {
                    // Sit out the rest of the cycle (or a whole one, if it is already over). An alert ends
                    // the wait at once; routine packets wait at most one cycle to be picked up.
                    var remaining = cycle - Stopwatch.GetElapsedTime(cycleStart);
                    await pipeline.WaitForAlertAsync(remaining > TimeSpan.Zero ? remaining : cycle, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        //----------------------------------------------------------//


        private void Process(IngestEnvelope envelope)
        {
            long processedAt = Stopwatch.GetTimestamp();
            double waitedMs = envelope.WaitedMilliseconds(processedAt);

            // For an alert, how many routine packets that arrived before it are still waiting - the ones it overtook.
            int bypassed = envelope.Lane == IngestLane.Alert ? pipeline.CountRoutineAheadOf(envelope.Sequence) : 0;

            var packet = envelope.Packet;
            string id = envelope.DeviceId;
            var now = DateTimeOffset.UtcNow;

            store.Add(id, packet);
            timeline.Append(id, packet.TimestampUtc, envelope.Sequence, packet.NumericValue);

            var outcome = registry.ApplyReading(id, packet, envelope.Assessment, envelope.ArrivedAtUtc);

            if (outcome.Known)
            {
                // Hearing from a node that was marked disconnected means it is back.
                if (alerts.MarkOnline(id))
                {
                    registry.SetOffline(id, false);
                    engine.OnConditionCleared(new ConditionKey(id, FaultKind.Offline));
                    events.Publish(StreamEventKind.DeviceReconnected, AlertSeverity.Info,
                        $"{outcome.DisplayName} reconnected", "Telemetry is flowing again.", id);
                }

                // A late, out-of-order reading is history - it must not raise or clear a live alert.
                if (outcome.IsNewest)
                {
                    var transition = alerts.Evaluate(id, outcome.DisplayName, envelope.Assessment, now);
                    Publish(envelope, outcome.DisplayName, transition, waitedMs, bypassed, now);
                }

                if (outcome.CommandAcknowledged && outcome.ReportedOn is { } on)
                {
                    events.Publish(StreamEventKind.CommandAcknowledged, AlertSeverity.Info,
                        $"{outcome.DisplayName} confirmed {envelope.Category.StateLabel(on)}",
                        "The device's reported state now matches the command.", id);
                }
            }

            pipeline.RecordProcessed(envelope, waitedMs, bypassed);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Turns a fault transition into history events and engine signals. Duplicates produce nothing:
        /// the alert tracker's set has already counted and absorbed them.
        /// </summary>
        private void Publish(IngestEnvelope envelope, string name, FaultTransition transition, double waitedMs, int bypassed, DateTimeOffset now)
        {
            string id = envelope.DeviceId;

            if (transition.ClearedKey is { } replaced)
            {
                engine.OnConditionCleared(new ConditionKey(replaced.DeviceId, replaced.Kind));
                events.Publish(StreamEventKind.AlertCleared, AlertSeverity.Info,
                    $"{name} · {ActionRecommendationEngine.Describe(replaced.Kind)} cleared", transition.ClearedDescription ?? string.Empty, id);
            }

            var condition = new ConditionKey(id, transition.Key.Kind);
            bool viaAlertLane = envelope.Lane == IngestLane.Alert;

            switch (transition.Change)
            {
                case FaultChange.Raised:
                    engine.OnConditionRaised(condition, transition.Severity, transition.Description, now);
                    events.Publish(StreamEventKind.AlertRaised, transition.Severity,
                        $"{name} · {Title(transition.Key.Kind, transition.Severity)}",
                        viaAlertLane
                            ? $"{transition.Description}. Bypassed the routine queue: processed {waitedMs:F1} ms after arrival, ahead of {bypassed} waiting packet{(bypassed == 1 ? "" : "s")}."
                            : transition.Description,
                        id, viaAlertLane, viaAlertLane ? waitedMs : null, viaAlertLane ? bypassed : null);
                    break;

                case FaultChange.Escalated:
                case FaultChange.Eased:
                    engine.OnConditionUpdated(condition, transition.Severity, transition.Description);
                    events.Publish(
                        transition.Change == FaultChange.Escalated ? StreamEventKind.AlertEscalated : StreamEventKind.AlertEased,
                        transition.Severity,
                        $"{name} · {(transition.Change == FaultChange.Escalated ? "escalated to" : "eased to")} {transition.Severity.ToString().ToLowerInvariant()}",
                        transition.Description,
                        id, viaAlertLane, viaAlertLane ? waitedMs : null, viaAlertLane ? bypassed : null);
                    break;

                case FaultChange.Duplicate:
                    // No new alert, but keep the engine's wording on the latest value ("Moisture low: 24.1%").
                    engine.OnConditionUpdated(condition, transition.Severity, transition.Description);
                    break;

                case FaultChange.Cleared:
                    engine.OnConditionCleared(condition);
                    events.Publish(StreamEventKind.AlertCleared, AlertSeverity.Info,
                        $"{name} back to normal", $"Cleared: {transition.Description}", id);
                    break;
            }
        }
        //----------------------------------------------------------//


        private static string Title(FaultKind kind, AlertSeverity severity) => (kind, severity) switch
        {
            (FaultKind.PowerSpike, AlertSeverity.Critical) => "severe power spike",
            (FaultKind.PowerSpike, _) => "high load",
            (FaultKind.MoistureDrop, AlertSeverity.Critical) => "critical moisture drop",
            (FaultKind.MoistureDrop, _) => "moisture low",
            (FaultKind.Overheating, AlertSeverity.Critical) => "overheating",
            (FaultKind.Overheating, _) => "running hot",
            _ => ActionRecommendationEngine.Describe(kind)
        };
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
