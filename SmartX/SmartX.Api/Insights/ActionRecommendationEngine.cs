using SmartX.Api.Commands;
using SmartX.Api.Streaming;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;
using SmartX.Shared.Streaming;

namespace SmartX.Api.Insights
{
    /// <summary>
    /// A condition the gateway has raised: "Moisture Sensor B has a moisture drop".
    /// </summary>
    public readonly record struct ConditionKey(string DeviceId, FaultKind Kind);
    //----------------------------------------------------------//


    public enum OperatorActionKind
    {
        Search,
        Command
    }
    //----------------------------------------------------------//


    /// <summary>
    /// Something an operator did: searched for a device, or commanded one (or a whole group).
    /// Target is a device id, "scope:AllValves" for a group command, or "query:..." for a search that
    /// didn't resolve to a single device. A record struct, so equal actions hash equally and can key
    /// dictionaries and sets directly.
    /// </summary>
    public readonly record struct ActionKey(OperatorActionKind Kind, string Target, OverrideAction? Verb)
    {
        public const string ScopePrefix = "scope:";
        public const string QueryPrefix = "query:";

        public bool IsGroup => Target.StartsWith(ScopePrefix, StringComparison.Ordinal);
        public bool IsFreeText => Target.StartsWith(QueryPrefix, StringComparison.Ordinal);
    }
    //----------------------------------------------------------//


    /// <summary>
    /// Everything the engine hands the dashboard on one pass.
    /// Highlights maps a device id to the reason it has been flagged before anyone searched for it.
    /// </summary>
    public sealed record InsightReport(
        IReadOnlyList<InsightDto> Insights,
        IReadOnlyDictionary<string, string> Highlights,
        IReadOnlyList<string> PredictedSearches,
        int ActionsObserved);
    //----------------------------------------------------------//


    /// <summary>
    /// The Automated Action Engine behind "Suggested Actions".
    ///
    /// It learns from what operators actually do, and from the live data, and combines four analyses:
    ///
    ///  1. Condition -> response learning. When a condition is raised it is pushed onto a sliding
    ///     window (a Queue, oldest at the front, expired episodes dequeued as time moves on). Every
    ///     search or command an operator makes is credited to each condition still in that window, at
    ///     most once per episode (a HashSet per episode guards that), building a co-occurrence matrix:
    ///     Dictionary&lt;condition, Dictionary&lt;action, count&gt;&gt;. That is how "operators search for
    ///     Pump A right after Moisture Sensor B drops below 20%" is learned. Confidence is
    ///     count / episodes, and only patterns with real support are suggested.
    ///
    ///  2. Action sequences. A first-order Markov chain over operator actions,
    ///     Dictionary&lt;action, Dictionary&lt;next action, count&gt;&gt;, predicts the likely next step
    ///     from the last one, and feeds the "anticipated searches" chips under the search box.
    ///
    ///  3. Zone rules. With no history yet for a condition, a pump in the same sub-zone as a drying
    ///     moisture sensor (or a relay sharing a spiking meter's circuit) is suggested instead - and a
    ///     shut valve on a drying bay is always flagged, since no pump can help until it opens.
    ///
    ///  4. Trend forecasts. A least-squares line through each device's recent, time-ordered readings
    ///     projects when it will cross a threshold - so the engine can suggest the learned response
    ///     (or the zone rule) before the alert has even fired.
    ///
    /// Every candidate gets a score, and the top few are chosen with a size-K min-heap
    /// (PriorityQueue): each candidate is pushed and, once the heap holds more than K, the weakest is
    /// popped - O(n log K) instead of sorting everything. Running a suggestion is itself an operator
    /// command, so it reinforces the pattern that produced it.
    /// </summary>
    public sealed class ActionRecommendationEngine
    {
        private static readonly TimeSpan ContextWindow = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan SequenceGap = TimeSpan.FromMinutes(10);

        private const int MinSupport = 2;
        private const double MinConfidence = 0.34;
        private const double MinSequenceProbability = 0.4;
        private const double ForecastHorizonMinutes = 15;
        private const int TopK = 6;

        private sealed class ConditionEpisode(long id, ConditionKey condition, DateTimeOffset raisedAtUtc)
        {
            public long Id { get; } = id;
            public ConditionKey Condition { get; } = condition;
            public DateTimeOffset RaisedAtUtc { get; } = raisedAtUtc;

            // Actions already credited to this episode - repeating a search must not inflate the count.
            public HashSet<ActionKey> Credited { get; } = new();
        }

        private sealed class PatternCount
        {
            public int Count { get; set; }
            public int LiveCount { get; set; }
        }

        private sealed record ActiveCondition(AlertSeverity Severity, string Description, DateTimeOffset RaisedAtUtc);

        private readonly Lock _gate = new();

        // Sliding window of recently raised conditions, oldest at the front.
        private readonly Queue<ConditionEpisode> _window = new();

        // condition -> how many times it has been raised.
        private readonly Dictionary<ConditionKey, int> _episodeCounts = new();

        // condition -> (operator action -> how many episodes it followed). The learned co-occurrence matrix.
        private readonly Dictionary<ConditionKey, Dictionary<ActionKey, PatternCount>> _responses = new();

        // action -> (next action -> count). The Markov chain over operator behaviour.
        private readonly Dictionary<ActionKey, Dictionary<ActionKey, int>> _transitions = new();

        // Conditions currently raised, which are what live suggestions respond to.
        private readonly Dictionary<ConditionKey, ActiveCondition> _active = new();

        private (ActionKey Action, DateTimeOffset AtUtc)? _lastAction;
        private long _nextEpisodeId;
        private int _actionsObserved;


        // ================================ Signals in ================================ //

        /// <summary>
        /// A condition has just become active. Opens a new episode in the sliding window.
        /// </summary>
        public void OnConditionRaised(ConditionKey condition, AlertSeverity severity, string description, DateTimeOffset atUtc)
        {
            lock (_gate)
            {
                Prune(atUtc);

                _episodeCounts[condition] = _episodeCounts.GetValueOrDefault(condition) + 1;
                _window.Enqueue(new ConditionEpisode(++_nextEpisodeId, condition, atUtc));
                _active[condition] = new ActiveCondition(severity, description, atUtc);
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// An active condition changed severity (e.g. moisture low -> critically low).
        /// </summary>
        public void OnConditionUpdated(ConditionKey condition, AlertSeverity severity, string description)
        {
            lock (_gate)
            {
                if (_active.TryGetValue(condition, out var active))
                    _active[condition] = active with { Severity = severity, Description = description };
            }
        }
        //----------------------------------------------------------//


        public void OnConditionCleared(ConditionKey condition)
        {
            lock (_gate)
            {
                _active.Remove(condition);
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// An operator searched. If the text pins down one device, the search is learned as
        /// "looked up that device"; otherwise as the query text itself.
        /// </summary>
        public void RecordSearch(string query, IReadOnlyList<DeviceSnapshot> resolved, DateTimeOffset atUtc)
        {
            string trimmed = query.Trim();
            if (trimmed.Length < 2)
                return;

            var action = resolved.Count == 1
                ? new ActionKey(OperatorActionKind.Search, resolved[0].Id, null)
                : new ActionKey(OperatorActionKind.Search, ActionKey.QueryPrefix + trimmed.ToLowerInvariant(), null);

            lock (_gate)
            {
                Observe(action, atUtc, live: true);
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// An operator issued a command - typed in themselves, or run from a suggestion card.
        /// </summary>
        public void RecordCommand(CommandRecordDto command, DateTimeOffset atUtc, bool live = true)
        {
            lock (_gate)
            {
                foreach (var action in ActionKeysFor(command))
                    Observe(action, atUtc, live);
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Replays a historical search during seeding, without counting it as this session's.
        /// </summary>
        public void RecordHistoricalSearch(string deviceId, DateTimeOffset atUtc)
        {
            lock (_gate)
            {
                Observe(new ActionKey(OperatorActionKind.Search, deviceId, null), atUtc, live: false);
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// The core learning step, run for every operator action (the caller holds the lock).
        /// </summary>
        private void Observe(ActionKey action, DateTimeOffset atUtc, bool live)
        {
            Prune(atUtc);

            // 1. Credit the action to every condition episode raised in the last few minutes - once each.
            foreach (var episode in _window)
            {
                if (episode.RaisedAtUtc > atUtc || !episode.Credited.Add(action))
                    continue;

                if (!_responses.TryGetValue(episode.Condition, out var responses))
                    _responses[episode.Condition] = responses = new Dictionary<ActionKey, PatternCount>();

                if (!responses.TryGetValue(action, out var pattern))
                    responses[action] = pattern = new PatternCount();

                pattern.Count++;
                if (live)
                    pattern.LiveCount++;
            }

            // 2. Extend the Markov chain from the previous action, if it was recent enough to be related.
            var sinceLast = _lastAction is { } previous ? atUtc - previous.AtUtc : TimeSpan.MaxValue;

            if (_lastAction is { } last && sinceLast >= TimeSpan.Zero && sinceLast <= SequenceGap && last.Action != action)
            {
                if (!_transitions.TryGetValue(last.Action, out var successors))
                    _transitions[last.Action] = successors = new Dictionary<ActionKey, int>();

                successors[action] = successors.GetValueOrDefault(action) + 1;
            }

            _lastAction = (action, atUtc);
            _actionsObserved++;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Drops episodes that have slid out of the context window. Oldest are at the front of the
        /// queue, so this stops at the first one still inside it.
        /// </summary>
        private void Prune(DateTimeOffset nowUtc)
        {
            while (_window.Count > 0 && nowUtc - _window.Peek().RaisedAtUtc > ContextWindow)
                _window.Dequeue();
        }
        //----------------------------------------------------------//


        private static IEnumerable<ActionKey> ActionKeysFor(CommandRecordDto command)
        {
            if (command.Scope == CommandScope.Device)
            {
                // Learn the outcome, not the button: a Toggle that switched Pump A on is "turn Pump A on".
                foreach (var change in command.Changes)
                    yield return new ActionKey(OperatorActionKind.Command, change.DeviceId, change.Next ? OverrideAction.TurnOn : OverrideAction.TurnOff);

                yield break;
            }

            var direction = command.Action != OverrideAction.Toggle
                ? command.Action
                : command.Changes.Count(change => change.Next) * 2 >= command.Changes.Count ? OverrideAction.TurnOn : OverrideAction.TurnOff;

            yield return new ActionKey(OperatorActionKind.Command, ActionKey.ScopePrefix + command.Scope, direction);
        }
        //----------------------------------------------------------//


        // ================================ Insights out ================================ //

        /// <summary>
        /// Runs every analysis against the current fleet and returns the top-ranked suggestions.
        /// </summary>
        public InsightReport Analyse(IReadOnlyList<DeviceSnapshot> devices, IReadOnlyDictionary<string, TrendFit> trends, DateTimeOffset nowUtc)
        {
            var byId = devices.ToDictionary(device => device.Id, DeviceIdentifier.Comparer);
            var candidates = new CandidateSet();
            var predicted = new Dictionary<string, double>(DeviceIdentifier.Comparer);
            int actionsObserved;

            lock (_gate)
            {
                Prune(nowUtc);

                AddConditionResponses(byId, devices, candidates, predicted);
                AddSequencePredictions(byId, devices, candidates, predicted, nowUtc);
                AddForecasts(byId, devices, trends, candidates);

                actionsObserved = _actionsObserved;
            }

            var top = candidates.TopK(TopK);

            // Flag the devices the top suggestions point at, so the fleet list can highlight them.
            var highlights = new Dictionary<string, string>(DeviceIdentifier.Comparer);
            foreach (var insight in top)
            {
                if (insight.DeviceId is not null)
                    highlights.TryAdd(insight.DeviceId, insight.Title);

                if (insight.Command?.DeviceId is { } target)
                    predicted[target] = Math.Max(predicted.GetValueOrDefault(target), insight.Score);
            }

            var predictedSearches = predicted
                .OrderByDescending(pair => pair.Value)
                .Select(pair => byId.TryGetValue(pair.Key, out var device) ? device.DisplayName : null)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            return new InsightReport(top, highlights, predictedSearches, actionsObserved);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// For each active condition: what operators have learned to do about it, or - with no history
        /// to go on - what the zone rules suggest.
        /// </summary>
        private void AddConditionResponses(
            Dictionary<string, DeviceSnapshot> byId,
            IReadOnlyList<DeviceSnapshot> devices,
            CandidateSet candidates,
            Dictionary<string, double> predicted)
        {
            foreach (var (condition, active) in _active)
            {
                if (!byId.TryGetValue(condition.DeviceId, out var subject))
                    continue;

                bool commandSuggested = false;

                if (_responses.TryGetValue(condition, out var responses))
                {
                    foreach (var (action, pattern) in responses)
                    {
                        var (count, episodes) = PastSupport(condition, action, pattern);
                        if (count < MinSupport)
                            continue;

                        double confidence = Math.Min(1, (double)count / episodes);
                        if (confidence < MinConfidence)
                            continue;

                        string evidence = $"Learned · {count} of {episodes} past episodes"
                            + (pattern.LiveCount > 0 ? $" ({pattern.LiveCount} this session)" : string.Empty);

                        if (action.Kind == OperatorActionKind.Command)
                        {
                            var command = ToSuggestedCommand(action, byId, devices);
                            if (command is null)
                                continue;

                            commandSuggested = true;

                            candidates.Add(new InsightDto(
                                $"learned|{condition.DeviceId}|{condition.Kind}|{action.Target}|{action.Verb}",
                                InsightKind.LearnedResponse,
                                active.Severity,
                                command.Label,
                                $"{subject.DisplayName}: {active.Description}. Operators responded with this in {count} of {episodes} similar episodes.",
                                evidence,
                                confidence,
                                Weight(active.Severity) * confidence * (1 + Math.Log2(1 + count)),
                                command.DeviceId ?? subject.Id,
                                command));
                        }
                        else if (!action.IsFreeText
                                 && !DeviceIdentifier.Comparer.Equals(action.Target, condition.DeviceId)
                                 && byId.TryGetValue(action.Target, out var lookedUp))
                        {
                            predicted[lookedUp.Id] = Math.Max(predicted.GetValueOrDefault(lookedUp.Id), 2 * confidence);

                            candidates.Add(new InsightDto(
                                $"check|{condition.DeviceId}|{condition.Kind}|{lookedUp.Id}",
                                InsightKind.LearnedResponse,
                                active.Severity,
                                $"Check {lookedUp.DisplayName}",
                                $"Operators looked up {lookedUp.DisplayName} in {count} of {episodes} past episodes where {subject.DisplayName} had a {Describe(condition.Kind)}.",
                                evidence,
                                confidence,
                                0.6 * Weight(active.Severity) * confidence * (1 + Math.Log2(1 + count)),
                                lookedUp.Id,
                                null));
                        }
                    }
                }

                // Zone rules fill the gap when there is no history - except a blocking one (a shut valve),
                // which is shown regardless because no learned pump command can work until it is fixed.
                if (ZoneRuleCommand(condition.Kind, subject, devices) is { } rule && (!commandSuggested || rule.Blocking))
                {
                    candidates.Add(new InsightDto(
                        $"rule|{condition.DeviceId}|{condition.Kind}|{rule.Command.DeviceId}",
                        InsightKind.ZoneRule,
                        active.Severity,
                        rule.Command.Label,
                        $"{subject.DisplayName}: {active.Description}. {rule.Reason}",
                        rule.Blocking ? "Zone rule · prerequisite for any irrigation" : "Zone rule · no operator history for this yet",
                        rule.Confidence,
                        Weight(active.Severity) * (rule.Blocking ? 3 : rule.Confidence * 1.2),
                        rule.Command.DeviceId,
                        rule.Command));
                }
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// What usually comes next after the operator's last action, from the Markov chain.
        /// </summary>
        private void AddSequencePredictions(
            Dictionary<string, DeviceSnapshot> byId,
            IReadOnlyList<DeviceSnapshot> devices,
            CandidateSet candidates,
            Dictionary<string, double> predicted,
            DateTimeOffset nowUtc)
        {
            if (_lastAction is not { } last || nowUtc - last.AtUtc > SequenceGap)
                return;

            if (!_transitions.TryGetValue(last.Action, out var successors))
                return;

            int total = successors.Values.Sum();

            foreach (var (next, count) in successors)
            {
                double probability = (double)count / total;
                if (count < MinSupport || probability < MinSequenceProbability)
                    continue;

                if (next.Kind == OperatorActionKind.Search)
                {
                    if (!next.IsFreeText && byId.ContainsKey(next.Target))
                        predicted[next.Target] = Math.Max(predicted.GetValueOrDefault(next.Target), 3 * probability);

                    continue;
                }

                var command = ToSuggestedCommand(next, byId, devices);
                if (command is null)
                    continue;

                candidates.Add(new InsightDto(
                    $"sequence|{last.Action.Target}|{next.Target}|{next.Verb}",
                    InsightKind.NextActionPrediction,
                    AlertSeverity.Info,
                    command.Label,
                    $"After you {DescribeAction(last.Action, byId)}, operators chose this next {count} of {total} times.",
                    $"Action sequence · P = {probability:P0}",
                    probability,
                    1.6 * probability * (1 + Math.Log2(1 + count)),
                    command.DeviceId,
                    command));
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Projects each trending device forward to the threshold it is heading for.
        /// </summary>
        private void AddForecasts(
            Dictionary<string, DeviceSnapshot> byId,
            IReadOnlyList<DeviceSnapshot> devices,
            IReadOnlyDictionary<string, TrendFit> trends,
            CandidateSet candidates)
        {
            foreach (var (deviceId, fit) in trends)
            {
                if (!byId.TryGetValue(deviceId, out var device) || device.IsOffline)
                    continue;

                if (fit.RSquared < 0.55 || Math.Abs(fit.SlopePerMinute) < 0.3)
                    continue;

                double current = device.LastNumeric ?? fit.LastValue;
                bool falling = fit.SlopePerMinute < 0;

                if (NextLimit(device.Category, current, falling) is not { } limit)
                    continue;

                double minutes = Math.Abs(current - limit.Value) / Math.Abs(fit.SlopePerMinute);
                if (minutes < 0.25 || minutes > ForecastHorizonMinutes)
                    continue;

                var forecastSeverity = limit.Critical ? AlertSeverity.Critical : AlertSeverity.Warning;
                var condition = new ConditionKey(device.Id, limit.Kind);

                // Already raised at this severity or worse - the alert itself covers it.
                if (_active.TryGetValue(condition, out var active) && active.Severity >= forecastSeverity)
                    continue;

                var command = BestLearnedCommand(condition, byId, devices) ?? ZoneRuleCommand(limit.Kind, device, devices)?.Command;
                string unit = device.Category.Unit();

                candidates.Add(new InsightDto(
                    $"forecast|{device.Id}|{limit.Kind}|{forecastSeverity}",
                    InsightKind.TrendForecast,
                    limit.Critical ? AlertSeverity.Warning : AlertSeverity.Info,
                    $"{device.DisplayName} heading for {limit.Value:0.#}{unit} in ~{FormatMinutes(minutes)}",
                    $"Trending {fit.SlopePerMinute:+0.0;-0.0} {unit}/min over the last {Math.Max(1, fit.WindowMinutes):F0} min (R² {fit.RSquared:F2}). "
                        + (command is not null ? $"Acting now heads off the alert: {command.Label}." : "No automatic response known yet - keep an eye on it."),
                    $"Least-squares trend · {fit.Points} readings from the sorted timeline",
                    fit.RSquared,
                    (limit.Critical ? 2.4 : 1.5) * fit.RSquared * (1 + (ForecastHorizonMinutes - minutes) / ForecastHorizonMinutes),
                    device.Id,
                    command));
            }
        }
        //----------------------------------------------------------//


        // ================================ Helpers ================================ //

        /// <summary>
        /// The best-supported command operators have used for a condition, if it would change anything now.
        /// </summary>
        private SuggestedCommandDto? BestLearnedCommand(ConditionKey condition, Dictionary<string, DeviceSnapshot> byId, IReadOnlyList<DeviceSnapshot> devices)
        {
            if (!_responses.TryGetValue(condition, out var responses))
                return null;

            return responses
                .Where(pair => pair.Key.Kind == OperatorActionKind.Command)
                .Select(pair => (Action: pair.Key, Support: PastSupport(condition, pair.Key, pair.Value)))
                .Where(entry => entry.Support.Count >= MinSupport
                                && (double)entry.Support.Count / entry.Support.Episodes >= MinConfidence)
                .OrderByDescending(entry => entry.Support.Count)
                .Select(entry => ToSuggestedCommand(entry.Action, byId, devices))
                .FirstOrDefault(command => command is not null);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// How often an action followed a condition in its *past* episodes. While a condition is active,
        /// its current episode hasn't been answered yet, so counting it would understate the pattern
        /// ("5 of 7" when operators actually did this in 5 of the 6 earlier episodes).
        /// </summary>
        private (int Count, int Episodes) PastSupport(ConditionKey condition, ActionKey action, PatternCount pattern)
        {
            int episodes = _episodeCounts.GetValueOrDefault(condition);
            int count = pattern.Count;

            if (_active.ContainsKey(condition))
            {
                episodes--;

                var current = _window.LastOrDefault(episode => episode.Condition == condition);
                if (current is not null && current.Credited.Contains(action))
                    count--;
            }

            return (count, Math.Max(1, episodes));
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Turns a learned action back into a runnable command - or null if it would do nothing,
        /// because the device is already in that state. Suggesting "Turn ON Pump A" while Pump A is
        /// already on would just be noise.
        /// </summary>
        private static SuggestedCommandDto? ToSuggestedCommand(ActionKey action, Dictionary<string, DeviceSnapshot> byId, IReadOnlyList<DeviceSnapshot> devices)
        {
            if (action.Verb is not { } verb || verb == OverrideAction.Toggle)
                return null;

            bool wantOn = verb == OverrideAction.TurnOn;

            if (action.IsGroup)
            {
                if (!Enum.TryParse<CommandScope>(action.Target[ActionKey.ScopePrefix.Length..], out var scope))
                    return null;

                var categories = CommandLabels.CategoriesFor(scope);
                bool wouldChange = devices.Any(device => categories.Contains(device.Category) && device.EffectiveOn != wantOn);

                return wouldChange ? new SuggestedCommandDto(verb, scope, null, CommandLabels.For(verb, scope)) : null;
            }

            if (!byId.TryGetValue(action.Target, out var target) || !target.Category.IsActuator() || target.EffectiveOn == wantOn)
                return null;

            return new SuggestedCommandDto(verb, CommandScope.Device, target.Id,
                CommandLabels.For(verb, CommandScope.Device, target.DisplayName, target.Category));
        }
        //----------------------------------------------------------//


        private sealed record ZoneRule(SuggestedCommandDto Command, string Reason, double Confidence, bool Blocking = false);


        /// <summary>
        /// Cold-start suggestions from the deployment layout: the pump nearest a drying (or flooding)
        /// moisture sensor, or the relay on the same circuit as a spiking meter.
        /// Same sub-zone is preferred over same zone; anything further away is not suggested.
        /// </summary>
        private static ZoneRule? ZoneRuleCommand(FaultKind kind, DeviceSnapshot subject, IReadOnlyList<DeviceSnapshot> devices)
        {
            // A drying bay whose own valve is shut can't be helped by any pump - open the valve first.
            if (kind == FaultKind.MoistureDrop)
            {
                var closedValve = devices.FirstOrDefault(device =>
                    device.Category == SensorCategory.Valve
                    && !device.EffectiveOn
                    && !device.IsOffline
                    && string.Equals(device.Location, subject.Location, StringComparison.OrdinalIgnoreCase));

                if (closedValve is not null)
                {
                    var open = new SuggestedCommandDto(OverrideAction.TurnOn, CommandScope.Device, closedValve.Id,
                        CommandLabels.For(OverrideAction.TurnOn, CommandScope.Device, closedValve.DisplayName, closedValve.Category));

                    return new ZoneRule(open, $"{closedValve.DisplayName} is closed, so no pump can get water to this bay until it opens.", 0.65, Blocking: true);
                }
            }

            (SensorCategory Category, bool WantOn, string Why)? rule = kind switch
            {
                FaultKind.MoistureDrop => (SensorCategory.WaterPump, true, "irrigates"),
                FaultKind.Overwatering => (SensorCategory.WaterPump, false, "is irrigating"),
                FaultKind.PowerSpike => (SensorCategory.Actuator, false, "is drawing load on"),
                _ => null
            };

            if (rule is not { } r)
                return null;

            var best = devices
                .Where(device => device.Category == r.Category && device.EffectiveOn != r.WantOn && !device.IsOffline)
                .Select(device => (Device: device, Affinity: Affinity(subject.Location, device.Location)))
                .Where(candidate => candidate.Affinity > 0)
                .OrderByDescending(candidate => candidate.Affinity)
                .ThenBy(candidate => candidate.Device.DisplayName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (best.Device is null)
                return null;

            var verb = r.WantOn ? OverrideAction.TurnOn : OverrideAction.TurnOff;
            var command = new SuggestedCommandDto(verb, CommandScope.Device, best.Device.Id,
                CommandLabels.For(verb, CommandScope.Device, best.Device.DisplayName, best.Device.Category));

            string place = best.Affinity >= 2 ? "sub-zone" : "zone";
            return new ZoneRule(command, $"{best.Device.DisplayName} {r.Why} the same {place}.", best.Affinity >= 2 ? 0.55 : 0.4);

            static int Affinity(string a, string b)
            {
                var left = a.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var right = b.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

                if (left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase))
                    return 2;

                return left.Length >= 2 && right.Length >= 2
                       && string.Equals(left[0], right[0], StringComparison.OrdinalIgnoreCase)
                       && string.Equals(left[1], right[1], StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 0;
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// The nearest threshold in the direction a value is moving, and the fault crossing it would raise.
        /// </summary>
        private static (double Value, bool Critical, FaultKind Kind)? NextLimit(SensorCategory category, double current, bool falling)
        {
            if (TelemetryRules.BandFor(category) is not { } band)
                return null;

            FaultKind? kind = (category, falling) switch
            {
                (SensorCategory.SoilMoisture, true) => FaultKind.MoistureDrop,
                (SensorCategory.SoilMoisture, false) => FaultKind.Overwatering,
                (SensorCategory.Environmental, _) => FaultKind.HumidityOutOfRange,
                (SensorCategory.PowerConsumption, false) => FaultKind.PowerSpike,
                (SensorCategory.Temperature, false) => FaultKind.Overheating,
                _ => null
            };

            if (kind is null)
                return null;

            var limits = falling
                ? new[] { (band.WarningLow, false), (band.CriticalLow, true) }
                    .Where(limit => limit.Item1 is { } value && value < current)
                    .OrderByDescending(limit => limit.Item1)
                : new[] { (band.WarningHigh, false), (band.CriticalHigh, true) }
                    .Where(limit => limit.Item1 is { } value && value > current)
                    .OrderBy(limit => limit.Item1);

            foreach (var (value, critical) in limits)
                return (value!.Value, critical, kind.Value);

            return null;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Every learned condition -> response pattern, strongest first, in words.
        /// </summary>
        public List<LearnedPatternDto> Patterns(IReadOnlyList<DeviceSnapshot> devices, int take)
        {
            var byId = devices.ToDictionary(device => device.Id, DeviceIdentifier.Comparer);

            lock (_gate)
            {
                return _responses
                    .SelectMany(condition => condition.Value.Select(response =>
                    {
                        var (count, episodes) = PastSupport(condition.Key, response.Key, response.Value);
                        return new LearnedPatternDto(
                            $"{NameOf(condition.Key.DeviceId, byId)} · {Describe(condition.Key.Kind)}",
                            Capitalise(DescribeAction(response.Key, byId)),
                            count,
                            episodes,
                            response.Value.LiveCount,
                            Math.Min(1, (double)count / episodes));
                    }))
                    .Where(pattern => pattern.Count >= MinSupport)
                    .OrderByDescending(pattern => pattern.Confidence)
                    .ThenByDescending(pattern => pattern.Count)
                    .Take(take)
                    .ToList();
            }
        }
        //----------------------------------------------------------//


        public int ActionsObserved
        {
            get { lock (_gate) return _actionsObserved; }
        }


        private static string DescribeAction(ActionKey action, Dictionary<string, DeviceSnapshot> byId)
        {
            if (action.Kind == OperatorActionKind.Search)
            {
                return action.IsFreeText
                    ? $"search \"{action.Target[ActionKey.QueryPrefix.Length..]}\""
                    : $"search for {NameOf(action.Target, byId)}";
            }

            if (action.IsGroup && Enum.TryParse<CommandScope>(action.Target[ActionKey.ScopePrefix.Length..], out var scope))
                return CommandLabels.For(action.Verb ?? OverrideAction.Toggle, scope).ToLowerInvariant();

            byId.TryGetValue(action.Target, out var device);
            string verb = CommandLabels.Verb(action.Verb ?? OverrideAction.Toggle, device?.Category);
            return $"{char.ToLowerInvariant(verb[0])}{verb[1..]} {NameOf(action.Target, byId)}";
        }
        //----------------------------------------------------------//


        private static string NameOf(string deviceId, Dictionary<string, DeviceSnapshot> byId) =>
            byId.TryGetValue(deviceId, out var device) ? device.DisplayName : deviceId;


        private static string Capitalise(string text) =>
            text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];


        public static string Describe(FaultKind kind) => kind switch
        {
            FaultKind.MoistureDrop => "moisture drop",
            FaultKind.Overwatering => "overwatering",
            FaultKind.PowerSpike => "power spike",
            FaultKind.HumidityOutOfRange => "humidity excursion",
            FaultKind.Overheating => "overheating",
            FaultKind.Offline => "dropout",
            _ => kind.ToString()
        };
        //----------------------------------------------------------//


        private static double Weight(AlertSeverity severity) => severity switch
        {
            AlertSeverity.Critical => 3,
            AlertSeverity.Warning => 2,
            _ => 1
        };


        private static string FormatMinutes(double minutes) =>
            minutes < 1 ? $"{Math.Max(1, minutes * 60):F0}s" : $"{minutes:F0} min";
        //----------------------------------------------------------//


        /// <summary>
        /// Collects candidate insights for one pass, keeping only the strongest suggestion for any one
        /// command, then picks the top K with a min-heap.
        /// </summary>
        private sealed class CandidateSet
        {
            private readonly Dictionary<string, InsightDto> _byId = new();
            private readonly Dictionary<string, string> _byCommand = new();


            public void Add(InsightDto insight)
            {
                if (_byId.TryGetValue(insight.Id, out var existing) && existing.Score >= insight.Score)
                    return;

                // Two analyses recommending the same command become one runnable card - the stronger one.
                // A forecast that loses keeps its card without the button: "heading for 20% in ~2 min"
                // is still worth reading even when another card already offers the fix.
                if (insight.Command is { } command)
                {
                    string commandKey = $"{command.Scope}|{command.DeviceId}|{command.Action}";

                    if (_byCommand.TryGetValue(commandKey, out var ownerId) && _byId.TryGetValue(ownerId, out var owner))
                    {
                        if (owner.Score >= insight.Score)
                        {
                            if (insight.Kind == InsightKind.TrendForecast)
                                _byId[insight.Id] = insight with { Command = null };

                            return;
                        }

                        if (owner.Kind == InsightKind.TrendForecast)
                            _byId[ownerId] = owner with { Command = null };
                        else
                            _byId.Remove(ownerId);
                    }

                    _byCommand[commandKey] = insight.Id;
                }

                _byId[insight.Id] = insight;
            }
            //----------------------------------------------------------//


            /// <summary>
            /// Size-K min-heap selection: the heap's root is always the weakest of the current best K,
            /// so each newcomer either replaces it or is discarded. Returned strongest first.
            /// </summary>
            public List<InsightDto> TopK(int k)
            {
                // A "Check Pump A" card adds nothing when another card already offers to command Pump A.
                var commanded = _byId.Values
                    .Where(insight => insight.Command?.DeviceId is not null)
                    .Select(insight => insight.Command!.DeviceId!)
                    .ToHashSet(DeviceIdentifier.Comparer);

                var heap = new PriorityQueue<InsightDto, double>();

                foreach (var insight in _byId.Values)
                {
                    if (insight.Id.StartsWith("check|", StringComparison.Ordinal) && insight.DeviceId is { } id && commanded.Contains(id))
                        continue;

                    heap.Enqueue(insight, insight.Score);

                    if (heap.Count > k)
                        heap.Dequeue();
                }

                var top = new List<InsightDto>(heap.Count);
                while (heap.TryDequeue(out var insight, out _))
                    top.Add(insight);

                top.Reverse();
                return top;
            }
            //----------------------------------------------------------//
        }
    }
}

//------------------------------------------- End Of File -------------------------------------------//
