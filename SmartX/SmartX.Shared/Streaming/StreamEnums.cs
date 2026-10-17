namespace SmartX.Shared.Streaming
{
    /// <summary>
    /// Which of the gateway's two ingestion queues a packet was routed into.
    /// Routine packets wait their turn in a FIFO Queue; alert packets go into a PriorityQueue that
    /// the processor always drains first.
    /// </summary>
    public enum IngestLane
    {
        Routine,
        Alert
    }
    //----------------------------------------------------------//


    /// <summary>
    /// How serious an out-of-range reading is. Declared in ascending order so a plain
    /// numeric comparison ranks them.
    /// </summary>
    public enum AlertSeverity
    {
        Info,
        Warning,
        Critical
    }
    //----------------------------------------------------------//


    /// <summary>
    /// The kind of error state a device can be in. Severity is tracked separately, so a moisture
    /// drop that worsens from 28% to 18% is one fault escalating, not two unrelated faults.
    /// </summary>
    public enum FaultKind
    {
        PowerSpike,
        MoistureDrop,
        Overwatering,
        HumidityOutOfRange,
        Overheating,
        Offline,
        CommandNotAcknowledged
    }
    //----------------------------------------------------------//


    /// <summary>
    /// The single state a device is filed under for the dashboard's alert-state filter.
    /// </summary>
    public enum DeviceAlertState
    {
        Normal,
        Warning,
        Critical,
        Offline,
        AwaitingData
    }
    //----------------------------------------------------------//


    /// <summary>
    /// What happened, for the live event stream and the sorted history log.
    /// </summary>
    public enum StreamEventKind
    {
        AlertRaised,
        AlertEscalated,
        AlertEased,
        AlertCleared,
        DeviceOffline,
        DeviceReconnected,
        CommandIssued,
        CommandUndone,
        CommandRedone,
        CommandAcknowledged,
        LoadTest,
        Info
    }
    //----------------------------------------------------------//


    /// <summary>
    /// The manual override verbs an operator can issue to an actuator.
    /// </summary>
    public enum OverrideAction
    {
        TurnOn,
        TurnOff,
        Toggle
    }
    //----------------------------------------------------------//


    /// <summary>
    /// What a command targets: one device, or every actuator of a kind at once.
    /// </summary>
    public enum CommandScope
    {
        Device,
        AllValves,
        AllPumps,
        AllRelays,
        AllActuators
    }
    //----------------------------------------------------------//


    /// <summary>
    /// Where a command came from, so the history shows which actions the operator took
    /// themselves and which were accepted from the suggestion engine.
    /// </summary>
    public enum CommandOrigin
    {
        Manual,
        Suggestion
    }
    //----------------------------------------------------------//


    /// <summary>
    /// The analysis that produced an automated insight.
    /// </summary>
    public enum InsightKind
    {
        LearnedResponse,
        NextActionPrediction,
        ZoneRule,
        TrendForecast,
        StatisticalAnomaly,
        Connectivity,
        CommandAcknowledgement
    }
    //----------------------------------------------------------//
}

//------------------------------------------- End Of File -------------------------------------------//
