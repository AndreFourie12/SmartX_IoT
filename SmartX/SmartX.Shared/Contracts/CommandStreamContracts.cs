using SmartX.Shared.Models;
using SmartX.Shared.Streaming;

namespace SmartX.Shared.Contracts
{
    /// <summary>
    /// Live state of one device, as cached in the gateway's device dictionary.
    /// DesiredOn/ReportedOn follow the device-shadow pattern: a command sets the desired state,
    /// and it only counts as done once the device itself reports the matching state.
    /// API -> Client
    /// </summary>
    public sealed record DeviceStateDto(
        string Id,
        string DisplayName,
        SensorCategory Category,
        string Location,
        DeviceAlertState AlertState,
        FaultKind? Fault,
        string? AlertReason,
        string? LastValue,
        double? LastNumeric,
        string Unit,
        DateTimeOffset? LastSeenUtc,
        bool IsActuator,
        bool? ReportedOn,
        bool? DesiredOn,
        bool CommandPending,
        IReadOnlyList<double> Sparkline,
        long PacketsProcessed,
        bool Highlighted,
        string? HighlightReason);
    //----------------------------------------------------------//


    /// <summary>
    /// How many devices sit in each category and alert state. Each figure is the size of an index
    /// set on the API, so the whole facet bar costs O(categories + states) to produce.
    /// </summary>
    public sealed record FilterFacetsDto(
        IReadOnlyDictionary<SensorCategory, int> Categories,
        IReadOnlyDictionary<DeviceAlertState, int> AlertStates);
    //----------------------------------------------------------//


    /// <summary>
    /// Depth, throughput and wait-time figures for the two ingestion queues.
    /// </summary>
    public sealed record PipelineStatsDto(
        int RoutineDepth,
        int AlertDepth,
        int RoutineCapacity,
        long RoutineProcessed,
        long AlertProcessed,
        double RoutineAvgWaitMs,
        double AlertAvgWaitMs,
        double RoutineMaxWaitMs,
        double AlertMaxWaitMs,
        long Shed,
        double ProcessedPerSecond,
        int RoutineBudgetPerSecond,
        int LastBypassCount);
    //----------------------------------------------------------//


    /// <summary>
    /// One unique error state held in the gateway's active-fault set.
    /// </summary>
    public sealed record ActiveFaultDto(
        string DeviceId,
        string DisplayName,
        FaultKind Kind,
        AlertSeverity Severity,
        string Description,
        DateTimeOffset RaisedAtUtc);
    //----------------------------------------------------------//


    /// <summary>
    /// Contents of the two HashSets: unique active faults and disconnected nodes, plus how many
    /// repeat alerts the sets have refused so far.
    /// </summary>
    public sealed record AlertSummaryDto(
        int ActiveFaults,
        int CriticalFaults,
        int DisconnectedNodes,
        long DuplicatesSuppressed,
        IReadOnlyList<ActiveFaultDto> Faults,
        IReadOnlyList<string> Disconnected);
    //----------------------------------------------------------//


    /// <summary>
    /// One entry in the live event stream / sorted history log.
    /// ViaPriorityLane, LatencyMs and Bypassed are filled in when the event came from a packet that
    /// was processed off the alert lane, which is the evidence that it really did skip the queue.
    /// </summary>
    public sealed record StreamEventDto(
        long Id,
        DateTimeOffset TimestampUtc,
        StreamEventKind Kind,
        AlertSeverity Severity,
        string Title,
        string Detail,
        string? DeviceId,
        bool ViaPriorityLane,
        double? LatencyMs,
        int? Bypassed);
    //----------------------------------------------------------//


    /// <summary>
    /// One timestamped point on a device's timeline.
    /// </summary>
    public sealed record TimelinePointDto(DateTimeOffset TimestampUtc, double Value, string Display);
    //----------------------------------------------------------//


    /// <summary>
    /// A device's timeline over a window, already in timestamp order because it is read straight
    /// out of a SortedDictionary. Band carries the thresholds so the chart can shade them.
    /// </summary>
    public sealed record TimelineDto(
        string DeviceId,
        string DisplayName,
        SensorCategory Category,
        string Unit,
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtc,
        IReadOnlyList<TimelinePointDto> Points,
        ThresholdBand? Band,
        int TotalStored);
    //----------------------------------------------------------//


    /// <summary>
    /// A manual override command. DeviceId is only used when Scope is Device.
    /// InsightId is set when the operator accepted a suggestion, so the engine can reinforce it.
    /// Client -> API
    /// </summary>
    public sealed record OverrideCommandRequest(
        OverrideAction Action,
        CommandScope Scope,
        string? DeviceId = null,
        CommandOrigin Origin = CommandOrigin.Manual,
        string? InsightId = null);
    //----------------------------------------------------------//


    /// <summary>
    /// The state change a command made to one actuator, kept so Undo can put it back.
    /// </summary>
    public sealed record DeviceChangeDto(string DeviceId, string DisplayName, bool Previous, bool Next);
    //----------------------------------------------------------//


    /// <summary>
    /// A command as it sits on the undo or redo stack.
    /// </summary>
    public sealed record CommandRecordDto(
        string Id,
        string Label,
        OverrideAction Action,
        CommandScope Scope,
        IReadOnlyList<DeviceChangeDto> Changes,
        DateTimeOffset IssuedAtUtc,
        CommandOrigin Origin);
    //----------------------------------------------------------//


    /// <summary>
    /// The top of both stacks, newest first.
    /// </summary>
    public sealed record CommandHistoryDto(
        IReadOnlyList<CommandRecordDto> UndoStack,
        int UndoCount,
        IReadOnlyList<CommandRecordDto> RedoStack,
        int RedoCount);
    //----------------------------------------------------------//


    /// <summary>
    /// Outcome of issuing, undoing or redoing a command.
    /// </summary>
    public sealed record CommandResultDto(
        bool Applied,
        string Message,
        CommandRecordDto? Command,
        CommandHistoryDto History);
    //----------------------------------------------------------//


    /// <summary>
    /// A command the engine is ready to run on the operator's behalf.
    /// </summary>
    public sealed record SuggestedCommandDto(
        OverrideAction Action,
        CommandScope Scope,
        string? DeviceId,
        string Label);
    //----------------------------------------------------------//


    /// <summary>
    /// One "Suggested Action" / "Automated Insight" card.
    /// </summary>
    public sealed record InsightDto(
        string Id,
        InsightKind Kind,
        AlertSeverity Severity,
        string Title,
        string Detail,
        string Evidence,
        double Confidence,
        double Score,
        string? DeviceId,
        SuggestedCommandDto? Command);
    //----------------------------------------------------------//


    /// <summary>
    /// A condition -> response pattern the engine has learned from operator history.
    /// </summary>
    public sealed record LearnedPatternDto(
        string Condition,
        string Response,
        int Count,
        int Episodes,
        int LiveCount,
        double Confidence);
    //----------------------------------------------------------//


    /// <summary>
    /// An operator search, logged so the engine can learn what operators look for and when.
    /// Client -> API
    /// </summary>
    public sealed record SearchLogRequest(string Query);
    //----------------------------------------------------------//


    /// <summary>
    /// Everything the Command Stream dashboard renders, in one round trip.
    /// API -> Client
    /// </summary>
    public sealed record StreamSnapshotDto(
        DateTimeOffset GeneratedAtUtc,
        PipelineStatsDto Pipeline,
        AlertSummaryDto Alerts,
        IReadOnlyList<DeviceStateDto> Devices,
        int TotalDevices,
        FilterFacetsDto Facets,
        IReadOnlyList<InsightDto> Insights,
        IReadOnlyList<LearnedPatternDto> Patterns,
        IReadOnlyList<string> PredictedSearches,
        int OperatorActionsLearned,
        CommandHistoryDto Commands,
        IReadOnlyList<StreamEventDto> RecentEvents,
        TimelineDto? SelectedTimeline);
    //----------------------------------------------------------//
}

//------------------------------------------- End Of File -------------------------------------------//
