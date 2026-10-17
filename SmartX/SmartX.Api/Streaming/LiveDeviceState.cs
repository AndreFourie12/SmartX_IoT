using SmartX.Api.Insights;
using SmartX.Shared.Collections;
using SmartX.Shared.Models;
using SmartX.Shared.Streaming;

namespace SmartX.Api.Streaming
{
    /// <summary>
    /// The gateway's cached, constantly-changing view of one device: its latest reading, fault,
    /// connectivity and (for actuators) desired versus reported state. One of these lives in the
    /// LiveDeviceRegistry dictionary per device and is updated in place on every packet, so
    /// answering "what is Pump A doing right now?" never touches telemetry history.
    /// </summary>
    public sealed class LiveDeviceState
    {
        public const int SparklineCapacity = 30;

        public required string Id { get; init; }
        public required string DisplayName { get; set; }
        public required SensorCategory Category { get; set; }
        public required string Location { get; set; }

        // When the gateway last heard from the device (arrival time, used for dropout detection).
        public DateTimeOffset? LastSeenUtc { get; set; }

        // The device timestamp of the newest reading, so a late, out-of-order packet can't overwrite it.
        public DateTimeOffset? LastReadingUtc { get; set; }

        public double? LastNumeric { get; set; }
        public long PacketsProcessed { get; set; }

        public FaultAssessment? CurrentFault { get; set; }
        public bool IsOffline { get; set; }

        // Device shadow: a command sets DesiredOn; the device confirms by reporting ReportedOn.
        public bool? ReportedOn { get; set; }
        public bool? DesiredOn { get; set; }
        public DateTimeOffset? DesiredSetAtUtc { get; set; }

        // Last 30 values for the dashboard sparkline - Part 1's ring buffer reused, O(1) per append.
        public TelemetryRingBuffer<double> Sparkline { get; } = new(SparklineCapacity);

        // A field rather than a property so Baseline.Add mutates the struct in place.
        public RollingBaseline Baseline;

        // Which alert-state index set this device currently sits in.
        public DeviceAlertState IndexedState { get; set; } = DeviceAlertState.AwaitingData;

        public bool EffectiveOn => DesiredOn ?? ReportedOn ?? false;
        public bool CommandPending => DesiredOn is { } desired && ReportedOn != desired;
    }
    //----------------------------------------------------------//


    /// <summary>
    /// Immutable copy of a device's live state, handed to code that runs outside the registry's lock
    /// (the recommendation engine, the engagement widget).
    /// </summary>
    public sealed record DeviceSnapshot(
        string Id,
        string DisplayName,
        SensorCategory Category,
        string Location,
        DeviceAlertState State,
        FaultAssessment? Fault,
        bool IsOffline,
        DateTimeOffset? LastSeenUtc,
        double? LastNumeric,
        bool? ReportedOn,
        bool? DesiredOn,
        DateTimeOffset? DesiredSetAtUtc,
        RollingBaseline Baseline)
    {
        public bool EffectiveOn => DesiredOn ?? ReportedOn ?? false;
        public bool CommandPending => DesiredOn is { } desired && ReportedOn != desired;
    }
    //----------------------------------------------------------//


    /// <summary>
    /// What changed when a reading was applied, so the processor knows which events to raise.
    /// IsNewest is false for a late packet whose device timestamp is older than one already seen.
    /// </summary>
    public readonly record struct ReadingOutcome(
        bool Known,
        bool IsNewest,
        string DisplayName,
        bool CommandAcknowledged,
        bool? ReportedOn,
        double? ZScore,
        double BaselineMean);
    //----------------------------------------------------------//


    /// <summary>
    /// Filter for the device list. Categories are OR'd together, alert states are OR'd together, and
    /// the two groups are AND'd, which is exactly a union within each group and an intersection between them.
    /// </summary>
    public sealed record DeviceQuery(
        string? Text,
        IReadOnlyCollection<SensorCategory>? Categories,
        IReadOnlyCollection<DeviceAlertState>? AlertStates);
    //----------------------------------------------------------//


    /// <summary>
    /// An actuator a command could act on, with the state Undo would need to restore.
    /// </summary>
    public sealed record ActuatorTarget(string Id, string DisplayName, SensorCategory Category, bool EffectiveOn);
    //----------------------------------------------------------//
}

//------------------------------------------- End Of File -------------------------------------------//
