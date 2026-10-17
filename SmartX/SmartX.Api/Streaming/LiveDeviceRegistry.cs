using System.Globalization;
using SmartX.Api.Commands;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;
using SmartX.Shared.Streaming;
using SmartX.Shared.Telemetry;

namespace SmartX.Api.Streaming
{
    /// <summary>
    /// The live device cache: a Dictionary keyed by MAC address / node identifier.
    ///
    /// Every telemetry packet that reaches the API is matched to its device here, so the lookup has
    /// to be O(1) whatever the fleet size - a List would mean scanning every device per packet.
    /// The dictionary is built with DeviceIdentifier.Comparer and every key passes through
    /// DeviceIdentifier.Normalize, so "24-6f-28-aa-bb-cc" and "24:6F:28:AA:BB:CC" hash to the same entry.
    ///
    /// Two secondary indexes sit beside it: category -> set of ids, and alert state -> set of ids.
    /// They are kept in step as devices change state, so the dashboard's filters are set lookups and
    /// intersections over a handful of small sets instead of a scan, and a group command ("Stop all
    /// pumps") reads its targets straight out of the category index.
    ///
    /// Dictionary and HashSet are not thread-safe, and this is written to by the packet processor,
    /// the health monitor and HTTP requests at once, so every member takes the same lock. Nothing
    /// slow ever runs inside it.
    /// </summary>
    public sealed class LiveDeviceRegistry
    {
        private readonly Lock _gate = new();

        private readonly Dictionary<string, LiveDeviceState> _devices = new(DeviceIdentifier.Comparer);
        private readonly Dictionary<SensorCategory, HashSet<string>> _byCategory = new();
        private readonly Dictionary<DeviceAlertState, HashSet<string>> _byAlertState = new();


        /// <summary>
        /// Creates an empty index set for every category and alert state up front, so lookups into
        /// the indexes never need a null check.
        /// </summary>
        public LiveDeviceRegistry()
        {
            foreach (var category in Enum.GetValues<SensorCategory>())
                _byCategory[category] = new HashSet<string>(DeviceIdentifier.Comparer);

            foreach (var state in Enum.GetValues<DeviceAlertState>())
                _byAlertState[state] = new HashSet<string>(DeviceIdentifier.Comparer);
        }
        //----------------------------------------------------------//


        public int Count
        {
            get { lock (_gate) return _devices.Count; }
        }


        /// <summary>
        /// Adds a device to the cache (or refreshes its details if it is already there) and files it
        /// under its category and alert-state indexes.
        /// </summary>
        public void Register(SensorDevice sensor)
        {
            lock (_gate)
            {
                if (_devices.TryGetValue(sensor.Id, out var existing))
                {
                    _byCategory[existing.Category].Remove(existing.Id);
                    existing.Category = sensor.Category;
                    existing.Location = sensor.Location;
                    existing.DisplayName = NameFor(sensor);
                    _byCategory[existing.Category].Add(existing.Id);
                    return;
                }

                var state = new LiveDeviceState
                {
                    Id = sensor.Id,
                    DisplayName = NameFor(sensor),
                    Category = sensor.Category,
                    Location = sensor.Location
                };

                _devices[state.Id] = state;
                _byCategory[state.Category].Add(state.Id);
                _byAlertState[state.IndexedState].Add(state.Id);
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// The O(1) lookup made when a packet arrives: is this a registered device, what category is it
        /// (which decides how the packet is classified), and what is its canonical id.
        /// </summary>
        public bool TryGetRouting(string id, out string canonicalId, out SensorCategory category)
        {
            lock (_gate)
            {
                if (_devices.TryGetValue(DeviceIdentifier.Normalize(id), out var state))
                {
                    canonicalId = state.Id;
                    category = state.Category;
                    return true;
                }
            }

            canonicalId = string.Empty;
            category = default;
            return false;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Folds one processed packet into the device's cached state.
        /// Arrival time always counts as "heard from", but only the newest reading by device timestamp
        /// moves the latest value and fault - a buffered packet flushed late after a
        /// reconnect must not make an old value look current.
        /// </summary>
        public ReadingOutcome ApplyReading(string id, ITelemetryPacket packet, FaultAssessment? assessment, DateTimeOffset arrivedAtUtc)
        {
            lock (_gate)
            {
                if (!_devices.TryGetValue(id, out var state))
                    return default;

                state.LastSeenUtc = arrivedAtUtc;
                state.PacketsProcessed++;

                bool isNewest = state.LastReadingUtc is null || packet.TimestampUtc >= state.LastReadingUtc;
                bool acknowledged = false;

                if (isNewest)
                {
                    double numeric = packet.NumericValue;

                    state.LastReadingUtc = packet.TimestampUtc;
                    state.LastNumeric = double.IsNaN(numeric) ? null : numeric;
                    state.CurrentFault = assessment;

                    if (state.Category.IsActuator() && !double.IsNaN(numeric))
                    {
                        bool wasPending = state.CommandPending;
                        state.ReportedOn = numeric >= 0.5;
                        acknowledged = wasPending && !state.CommandPending;
                    }
                }

                Reindex(state);
                return new ReadingOutcome(true, isNewest, state.DisplayName, acknowledged, state.ReportedOn);
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Moves a device in or out of the Offline index. Returns its display name for event text.
        /// </summary>
        public string SetOffline(string id, bool offline)
        {
            lock (_gate)
            {
                if (!_devices.TryGetValue(id, out var state))
                    return id;

                state.IsOffline = offline;
                Reindex(state);
                return state.DisplayName;
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Devices that have reported at least once but not within the threshold.
        /// Already-offline devices are included on purpose: the alert tracker's HashSet is what decides
        /// they are duplicates, and counts them as such.
        /// </summary>
        public List<string> SilentDevices(DateTimeOffset now, TimeSpan threshold)
        {
            lock (_gate)
            {
                var silent = new List<string>();

                foreach (var state in _devices.Values)
                {
                    if (state.LastSeenUtc is { } seen && now - seen > threshold)
                        silent.Add(state.Id);
                }

                return silent;
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Resolves which actuators a command applies to, along with the state each is in now.
        /// </summary>
        public List<ActuatorTarget> ActuatorsFor(CommandScope scope, string? deviceId)
        {
            lock (_gate)
            {
                if (scope == CommandScope.Device)
                {
                    if (deviceId is not null
                        && _devices.TryGetValue(DeviceIdentifier.Normalize(deviceId), out var single)
                        && single.Category.IsActuator())
                    {
                        return [ToTarget(single)];
                    }

                    return [];
                }

                // A group command reads straight out of the category index rather than scanning the fleet.
                return CommandLabels.CategoriesFor(scope)
                    .SelectMany(category => _byCategory[category])
                    .Select(id => ToTarget(_devices[id]))
                    .OrderBy(target => target.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            static ActuatorTarget ToTarget(LiveDeviceState state) =>
                new(state.Id, state.DisplayName, state.Category, state.EffectiveOn);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Records the state a command wants an actuator in. The device itself confirms it later by reporting.
        /// </summary>
        public void SetDesired(string id, bool on, DateTimeOffset atUtc)
        {
            lock (_gate)
            {
                if (!_devices.TryGetValue(id, out var state))
                    return;

                state.DesiredOn = on;
                state.DesiredSetAtUtc = atUtc;
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// The desired state of every commanded actuator - the "device shadow" the simulated devices
        /// poll to learn what they have been told to do.
        /// </summary>
        public Dictionary<string, bool> DesiredStates()
        {
            lock (_gate)
            {
                var desired = new Dictionary<string, bool>(DeviceIdentifier.Comparer);

                foreach (var id in _byCategory[SensorCategory.Valve]
                             .Concat(_byCategory[SensorCategory.WaterPump])
                             .Concat(_byCategory[SensorCategory.Actuator]))
                {
                    if (_devices[id].DesiredOn is { } on)
                        desired[id] = on;
                }

                return desired;
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Runs the dashboard's search and filters.
        /// Chosen categories are unioned, chosen alert states are unioned, then the two are intersected -
        /// all on the index sets, so a filter that matches 3 devices out of 10,000 only touches those 3
        /// before the free-text match runs.
        /// </summary>
        public List<DeviceStateDto> Query(DeviceQuery query, IReadOnlyDictionary<string, string> highlights)
        {
            lock (_gate)
            {
                HashSet<string>? candidates = null;

                if (query.Categories is { Count: > 0 })
                {
                    candidates = new HashSet<string>(DeviceIdentifier.Comparer);
                    foreach (var category in query.Categories)
                        candidates.UnionWith(_byCategory[category]);
                }

                if (query.AlertStates is { Count: > 0 })
                {
                    var inState = new HashSet<string>(DeviceIdentifier.Comparer);
                    foreach (var alertState in query.AlertStates)
                        inState.UnionWith(_byAlertState[alertState]);

                    if (candidates is null)
                        candidates = inState;
                    else
                        candidates.IntersectWith(inState);
                }

                IEnumerable<LiveDeviceState> matches = candidates is null
                    ? _devices.Values
                    : candidates.Select(id => _devices[id]);

                string text = query.Text?.Trim() ?? string.Empty;
                if (text.Length > 0)
                    matches = matches.Where(state => Matches(state, text));

                return matches
                    .OrderBy(state => DisplayRank(state, highlights))
                    .ThenBy(state => state.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(state => ToDto(state, highlights))
                    .ToList();
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Turns an operator's search text into the devices it refers to.
        /// An exact name or id wins outright; otherwise the text has to appear in the device's
        /// name, id, location or category.
        /// </summary>
        public List<DeviceSnapshot> Resolve(string text)
        {
            lock (_gate)
            {
                string trimmed = text.Trim();

                if (_devices.TryGetValue(DeviceIdentifier.Normalize(trimmed), out var byId))
                    return [ToSnapshot(byId)];

                var byName = _devices.Values.FirstOrDefault(state => string.Equals(state.DisplayName, trimmed, StringComparison.OrdinalIgnoreCase));
                if (byName is not null)
                    return [ToSnapshot(byName)];

                return trimmed.Length == 0
                    ? []
                    : _devices.Values.Where(state => Matches(state, trimmed)).Select(ToSnapshot).ToList();
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// A consistent copy of every device for code that should not hold the registry lock.
        /// </summary>
        public List<DeviceSnapshot> Snapshot()
        {
            lock (_gate)
            {
                return _devices.Values.Select(ToSnapshot).ToList();
            }
        }
        //----------------------------------------------------------//


        public bool TryGetSnapshot(string id, out DeviceSnapshot snapshot)
        {
            lock (_gate)
            {
                if (_devices.TryGetValue(DeviceIdentifier.Normalize(id), out var state))
                {
                    snapshot = ToSnapshot(state);
                    return true;
                }
            }

            snapshot = null!;
            return false;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Recomputes which alert state a device belongs in and, only if it changed, moves its id from
        /// the old index set to the new one. Two O(1) set operations.
        /// </summary>
        private void Reindex(LiveDeviceState state)
        {
            var current = state switch
            {
                { IsOffline: true } => DeviceAlertState.Offline,
                { LastSeenUtc: null } => DeviceAlertState.AwaitingData,
                { CurrentFault.Severity: AlertSeverity.Critical } => DeviceAlertState.Critical,
                { CurrentFault: not null } => DeviceAlertState.Warning,
                _ => DeviceAlertState.Normal
            };

            if (current == state.IndexedState)
                return;

            _byAlertState[state.IndexedState].Remove(state.Id);
            _byAlertState[current].Add(state.Id);
            state.IndexedState = current;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Sort order for the fleet list: the devices most likely to need attention float to the top.
        /// </summary>
        private static int DisplayRank(LiveDeviceState state, IReadOnlyDictionary<string, string> highlights) => state.IndexedState switch
        {
            DeviceAlertState.Critical => 0,
            DeviceAlertState.Offline => 1,
            DeviceAlertState.Warning => 2,
            _ when highlights.ContainsKey(state.Id) => 3,
            DeviceAlertState.AwaitingData => 5,
            _ => 4
        };
        //----------------------------------------------------------//


        /// <summary>
        /// The search text, case-insensitively, anywhere in the device's name, id, location or category.
        /// </summary>
        private static bool Matches(LiveDeviceState state, string text) =>
            state.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase)
            || state.Id.Contains(text, StringComparison.OrdinalIgnoreCase)
            || state.Location.Contains(text, StringComparison.OrdinalIgnoreCase)
            || state.Category.DisplayName().Contains(text, StringComparison.OrdinalIgnoreCase);
        //----------------------------------------------------------//


        private static string NameFor(SensorDevice sensor) =>
            string.IsNullOrWhiteSpace(sensor.DisplayName) ? sensor.Id : sensor.DisplayName.Trim();


        private static DeviceSnapshot ToSnapshot(LiveDeviceState state) => new(
            state.Id,
            state.DisplayName,
            state.Category,
            state.Location,
            state.IndexedState,
            state.CurrentFault,
            state.IsOffline,
            state.LastSeenUtc,
            state.LastNumeric,
            state.ReportedOn,
            state.DesiredOn,
            state.DesiredSetAtUtc);


        private static DeviceStateDto ToDto(LiveDeviceState state, IReadOnlyDictionary<string, string> highlights)
        {
            highlights.TryGetValue(state.Id, out var reason);

            return new DeviceStateDto(
                state.Id,
                state.DisplayName,
                state.Category,
                state.Location,
                state.IndexedState,
                state.IsOffline ? FaultKind.Offline : state.CurrentFault?.Kind,
                state.IsOffline ? "No telemetry received - node offline" : state.CurrentFault?.Description,
                FormatValue(state.Category, state.LastNumeric),
                state.LastNumeric,
                state.Category.Unit(),
                state.LastSeenUtc,
                state.Category.IsActuator(),
                state.ReportedOn,
                state.DesiredOn,
                state.CommandPending,
                state.PacketsProcessed,
                reason is not null,
                reason);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Display text for a value: one decimal for floats, whole watts, and OPEN/CLOSED or ON/OFF for actuators.
        /// </summary>
        public static string? FormatValue(SensorCategory category, double? value) => value switch
        {
            null => null,
            double v when category.IsActuator() => category.StateLabel(v >= 0.5),
            double v when category.DataType() == TelemetryDataType.Int => v.ToString("F0", CultureInfo.InvariantCulture),
            double v => v.ToString("F1", CultureInfo.InvariantCulture)
        };
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
