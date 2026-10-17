using SmartX.Shared.Contracts;
using SmartX.Shared.Models;
using SmartX.Shared.Streaming;

namespace SmartX.Api.Streaming
{
    /// <summary>
    /// One unique error state: a device and the kind of fault it has. A record struct, so two keys
    /// with the same device and kind are equal and hash identically - which is what lets a HashSet
    /// recognise the second "Pump A is offline" as the same alert it already holds.
    /// </summary>
    public readonly record struct FaultKey(string DeviceId, FaultKind Kind);
    //----------------------------------------------------------//


    /// <summary>
    /// How a reading changed a device's fault picture.
    /// </summary>
    public enum FaultChange
    {
        None,
        Raised,
        Escalated,
        Eased,
        Duplicate,
        Cleared
    }
    //----------------------------------------------------------//


    /// <summary>
    /// The result of evaluating one reading. A reading can clear one fault and raise another at once
    /// (moisture swinging from too dry to too wet), so the clearing is reported separately.
    /// </summary>
    public readonly record struct FaultTransition(
        FaultChange Change,
        FaultKey Key,
        AlertSeverity Severity,
        string Description,
        FaultKey? ClearedKey,
        string? ClearedDescription);
    //----------------------------------------------------------//


    /// <summary>
    /// Tracks currently active, unique error states and disconnected nodes with two HashSets.
    ///
    /// A soil sensor stuck at 15% reports a critical reading every three seconds, and a dead node is
    /// noticed as silent on every health sweep. Without de-duplication each of those would become a
    /// fresh alert and the operator would be buried in copies. Here each condition is added to a set
    /// once: HashSet.Add returns false for an element already present, in O(1), and that false is the
    /// signal to suppress the repeat. The alert is raised exactly once, and again only after the
    /// condition has cleared (been removed from the set) and come back.
    ///
    /// The suppressed repeats are counted, so the dashboard can show how much noise the sets absorbed.
    /// </summary>
    public sealed class AlertTracker
    {
        private sealed record FaultInfo(string DisplayName, AlertSeverity Severity, string Description, DateTimeOffset RaisedAtUtc);

        private readonly Lock _gate = new();

        // The unique, currently active error states.
        private readonly HashSet<FaultKey> _activeFaults = new();

        // Currently disconnected nodes.
        private readonly HashSet<string> _disconnected = new(DeviceIdentifier.Comparer);

        // Details for each active fault, and the fault each device currently has, for O(1) lookups.
        private readonly Dictionary<FaultKey, FaultInfo> _details = new();
        private readonly Dictionary<string, FaultKey> _faultByDevice = new(DeviceIdentifier.Comparer);

        private long _duplicatesSuppressed;


        public long DuplicatesSuppressed => Interlocked.Read(ref _duplicatesSuppressed);


        /// <summary>
        /// Active critical faults - what the navigation bar's red dot counts.
        /// </summary>
        public int CriticalCount
        {
            get
            {
                lock (_gate)
                    return _details.Values.Count(info => info.Severity == AlertSeverity.Critical);
            }
        }


        /// <summary>
        /// Folds the latest reading's verdict into the active-fault set.
        /// </summary>
        public FaultTransition Evaluate(string deviceId, string displayName, FaultAssessment? assessment, DateTimeOffset nowUtc)
        {
            lock (_gate)
            {
                FaultKey? previous = _faultByDevice.TryGetValue(deviceId, out var existing) ? existing : null;

                // Healthy reading: if the device had a fault, it is over.
                if (assessment is not { } current)
                {
                    if (previous is not { } stale)
                        return default;

                    string description = _details[stale].Description;
                    RemoveFault(stale);
                    return new FaultTransition(FaultChange.Cleared, stale, AlertSeverity.Info, description, null, null);
                }

                var key = new FaultKey(deviceId, current.Kind);

                // A different kind of fault replaces the old one: clear it first.
                FaultKey? clearedKey = null;
                string? clearedDescription = null;
                if (previous is { } old && old != key)
                {
                    clearedKey = old;
                    clearedDescription = _details[old].Description;
                    RemoveFault(old);
                }

                // The set decides: Add is true only the first time this device/kind pair becomes active.
                if (_activeFaults.Add(key))
                {
                    _details[key] = new FaultInfo(displayName, current.Severity, current.Description, nowUtc);
                    _faultByDevice[deviceId] = key;
                    return new FaultTransition(FaultChange.Raised, key, current.Severity, current.Description, clearedKey, clearedDescription);
                }

                // Already active. Same severity is a duplicate; a change of severity is worth telling the operator.
                var info = _details[key];
                _details[key] = info with { Description = current.Description, Severity = current.Severity };

                if (current.Severity > info.Severity)
                    return new FaultTransition(FaultChange.Escalated, key, current.Severity, current.Description, null, null);

                if (current.Severity < info.Severity)
                    return new FaultTransition(FaultChange.Eased, key, current.Severity, current.Description, null, null);

                _duplicatesSuppressed++;
                return new FaultTransition(FaultChange.Duplicate, key, current.Severity, current.Description, null, null);
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Called by each health sweep with every device currently silent past the threshold.
        /// Set algebra does the work: silent minus already-disconnected is the newly-offline list;
        /// silent intersected with already-disconnected are repeats, counted and dropped.
        /// Returns only the newly-offline nodes, which are the only ones that deserve an alert.
        /// </summary>
        public List<string> RecordSilent(IReadOnlyCollection<string> silentNow)
        {
            lock (_gate)
            {
                var newlyOffline = new HashSet<string>(silentNow, DeviceIdentifier.Comparer);
                newlyOffline.ExceptWith(_disconnected);

                _duplicatesSuppressed += silentNow.Count - newlyOffline.Count;
                _disconnected.UnionWith(newlyOffline);

                return newlyOffline.ToList();
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// A packet arrived from this device. True if it had been marked disconnected, i.e. it has
        /// just come back - Remove reports whether the element was there, so this is one O(1) call.
        /// </summary>
        public bool MarkOnline(string deviceId)
        {
            lock (_gate)
            {
                return _disconnected.Remove(deviceId);
            }
        }
        //----------------------------------------------------------//


        public bool IsDisconnected(string deviceId)
        {
            lock (_gate)
            {
                return _disconnected.Contains(deviceId);
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// The two sets' contents for the dashboard, worst first.
        /// </summary>
        public AlertSummaryDto Summary(IReadOnlyDictionary<string, string> displayNames)
        {
            lock (_gate)
            {
                var faults = _activeFaults
                    .Select(key =>
                    {
                        var info = _details[key];
                        return new ActiveFaultDto(key.DeviceId, info.DisplayName, key.Kind, info.Severity, info.Description, info.RaisedAtUtc);
                    })
                    .OrderByDescending(fault => fault.Severity)
                    .ThenBy(fault => fault.RaisedAtUtc)
                    .ToList();

                var disconnected = _disconnected
                    .Select(id => displayNames.TryGetValue(id, out var name) ? name : id)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new AlertSummaryDto(
                    faults.Count,
                    faults.Count(fault => fault.Severity == AlertSeverity.Critical),
                    disconnected.Count,
                    _duplicatesSuppressed,
                    faults,
                    disconnected);
            }
        }
        //----------------------------------------------------------//


        private void RemoveFault(FaultKey key)
        {
            _activeFaults.Remove(key);
            _details.Remove(key);
            _faultByDevice.Remove(key.DeviceId);
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
