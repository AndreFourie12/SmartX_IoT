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
    /// They are kept in step as devices change state, so the dashboard's filters are set unions and
    /// intersections over a handful of small sets instead of a scan, and each filter-chip count is
    /// just the size of a set.
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
        /// moves the latest value, sparkline and fault - a buffered packet flushed late after a
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
                double? zScore = null;

                if (isNewest)
                {
                    double numeric = packet.NumericValue;

                    state.LastReadingUtc = packet.TimestampUtc;
                    state.LastNumeric = double.IsNaN(numeric) ? null : numeric;
                    state.CurrentFault = assessment;

                    if (!double.IsNaN(numeric))
                    {
                        state.Sparkline.Add(numeric);

                        // Score the reading against the baseline before it joins it. Soil moisture is skipped: it is
                        // driven by the pumps, so a "normal" level for it doesn't exist the way it does for a meter.
                        if (!state.Category.IsActuator() && state.Category != SensorCategory.SoilMoisture)
                            zScore = state.Baseline.ZScore(numeric);

                        // Only healthy readings teach the baseline, so a spike can't drag "normal" towards itself.
                        if (assessment is null && !state.Category.IsActuator())
                            state.Baseline.Add(numeric);
                    }

                    if (state.Category.IsActuator() && !double.IsNaN(numeric))
                    {
                        bool wasPending = state.CommandPending;
                        state.ReportedOn = numeric >= 0.5;
                        acknowledged = wasPending && !state.CommandPending;
                    }
                }

                Reindex(state);
                return new ReadingOutcome(true, isNewest, state.DisplayName, acknowledged, state.ReportedOn, zScore, state.Baseline.Mean);
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
        /// Category chips are unioned, alert-state chips are unioned, then the two are intersected -
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

                string[] tokens = Tokenize(query.Text);
                if (tokens.Length > 0)
                    matches = matches.Where(state => MatchesAll(state, tokens));

                return matches
                    .OrderBy(state => DisplayRank(state, highlights))
                    .ThenBy(state => state.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(state => ToDto(state, highlights))
                    .ToList();
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Device counts per category and per alert state, each read off the size of its index set.
        /// </summary>
        public FilterFacetsDto Facets()
        {
            lock (_gate)
            {
                return new FilterFacetsDto(
                    _byCategory.Where(pair => pair.Value.Count > 0).ToDictionary(pair => pair.Key, pair => pair.Value.Count),
                    _byAlertState.ToDictionary(pair => pair.Key, pair => pair.Value.Count));
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Turns an operator's search text into the devices it refers to.
        /// An exact name or id wins outright; otherwise every token has to appear in the device's
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

                string[] tokens = Tokenize(trimmed);
                return tokens.Length == 0
                    ? []
                    : _devices.Values.Where(state => MatchesAll(state, tokens)).Select(ToSnapshot).ToList();
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


        private static string[] Tokenize(string? text) =>
            string.IsNullOrWhiteSpace(text)
                ? []
                : text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);


        private static readonly char[] WordSeparators = [' ', '/', '-', ':', '_'];


        /// <summary>
        /// Every search word has to match the start of a word in the device's name, id, location or
        /// category. Matching word starts rather than any substring is what stops "bay 2" from also
        /// finding ESP32-VLV-01 (the "2" in "ESP32").
        /// </summary>
        private static bool MatchesAll(LiveDeviceState state, string[] tokens)
        {
            foreach (var token in tokens)
            {
                bool hit = WordStartsWith(state.DisplayName, token)
                    || WordStartsWith(state.Id, token)
                    || WordStartsWith(state.Location, token)
                    || WordStartsWith(state.Category.DisplayName(), token);

                if (!hit)
                    return false;
            }

            return true;
        }
        //----------------------------------------------------------//


        private static bool WordStartsWith(string field, string token)
        {
            // A token that itself spans words ("VLV-02", "24:6F") is matched as a phrase.
            if (token.IndexOfAny(WordSeparators) >= 0)
                return field.Contains(token, StringComparison.OrdinalIgnoreCase);

            foreach (var word in field.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
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
            state.DesiredSetAtUtc,
            state.Baseline);


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
                state.Sparkline.ToList(),
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
