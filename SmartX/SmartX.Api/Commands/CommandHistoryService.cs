using SmartX.Api.Insights;
using SmartX.Api.Streaming;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;
using SmartX.Shared.Streaming;

namespace SmartX.Api.Commands
{
    /// <summary>
    /// Issues manual override commands and keeps their history on a stack.
    ///
    /// Undo is naturally last-in, first-out: the command to revert is always the most recent one, so
    /// the history is a Stack&lt;T&gt; and Undo is a Pop - O(1), no searching. Every command records the
    /// state each actuator was in before it ran, so undoing "Shut down all valves" puts every valve
    /// back exactly as it was, including any that were already closed.
    ///
    /// A command that would change nothing (closing valves that are already closed) is refused and
    /// never pushed, so Undo can't land on a step that does nothing visible.
    /// </summary>
    public sealed class CommandHistoryService(LiveDeviceRegistry registry, StreamEventLog events, ActionRecommendationEngine engine)
    {
        private const int HistoryShown = 8;

        private sealed record CommandRecord(
            string Id,
            string Label,
            OverrideAction Action,
            CommandScope Scope,
            IReadOnlyList<DeviceChangeDto> Changes,
            DateTimeOffset IssuedAtUtc,
            CommandOrigin Origin);

        private readonly Lock _gate = new();
        private readonly Stack<CommandRecord> _undo = new();
        private int _nextId;


        /// <summary>
        /// Works out which actuators the request reaches and what each should become, applies it to the
        /// device shadow, and pushes the command onto the undo stack.
        /// </summary>
        public CommandResultDto Execute(OverrideCommandRequest request)
        {
            var now = DateTimeOffset.UtcNow;
            CommandRecord record;

            lock (_gate)
            {
                var targets = registry.ActuatorsFor(request.Scope, request.DeviceId);

                if (targets.Count == 0)
                {
                    string reason = request.Scope == CommandScope.Device
                        ? $"'{request.DeviceId}' is not a registered pump, valve or relay."
                        : "There are no actuators in that group.";

                    return Refused(reason);
                }

                var changes = targets
                    .Select(target => new DeviceChangeDto(target.Id, target.DisplayName, target.EffectiveOn, NextState(request.Action, target.EffectiveOn)))
                    .Where(change => change.Previous != change.Next)
                    .ToList();

                if (changes.Count == 0)
                    return Refused("Every targeted device is already in that state - nothing to change, so nothing was added to the undo history.");

                var single = request.Scope == CommandScope.Device ? targets[0] : null;
                string label = CommandLabels.For(request.Action, request.Scope, single?.DisplayName, single?.Category);

                record = new CommandRecord($"C{++_nextId:000}", label, request.Action, request.Scope, changes, now, request.Origin);

                foreach (var change in changes)
                    registry.SetDesired(change.DeviceId, change.Next, now);

                _undo.Push(record);
            }

            var dto = ToDto(record);

            events.Publish(
                StreamEventKind.CommandIssued,
                AlertSeverity.Info,
                record.Label,
                $"{Describe(record.Changes, forward: true)}{(record.Origin == CommandOrigin.Suggestion ? " · accepted from Suggested Actions" : string.Empty)}",
                record.Changes.Count == 1 ? record.Changes[0].DeviceId : null);

            engine.RecordCommand(dto, now);

            return new CommandResultDto(true, $"{record.Label} sent to {Plural(record.Changes.Count, "device")}.", dto, History());
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Pops the most recent command and restores every device it touched to its previous state.
        /// </summary>
        public CommandResultDto Undo()
        {
            CommandRecord record;

            lock (_gate)
            {
                if (!_undo.TryPop(out record!))
                    return Refused("Nothing to undo.");

                var now = DateTimeOffset.UtcNow;
                foreach (var change in record.Changes)
                    registry.SetDesired(change.DeviceId, change.Previous, now);
            }

            events.Publish(
                StreamEventKind.CommandUndone,
                AlertSeverity.Info,
                $"Undo: {record.Label}",
                Describe(record.Changes, forward: false),
                record.Changes.Count == 1 ? record.Changes[0].DeviceId : null);

            return new CommandResultDto(true, $"Undid \"{record.Label}\" - restored {Plural(record.Changes.Count, "device")}.", ToDto(record), History());
        }
        //----------------------------------------------------------//


        /// <summary>
        /// The top of the undo stack, newest first. Enumerating a Stack yields in pop order, so the first
        /// item is always the one Undo would act on next.
        /// </summary>
        public CommandHistoryDto History()
        {
            lock (_gate)
            {
                return new CommandHistoryDto(
                    _undo.Take(HistoryShown).Select(ToDto).ToList(),
                    _undo.Count);
            }
        }
        //----------------------------------------------------------//


        private static bool NextState(OverrideAction action, bool current) => action switch
        {
            OverrideAction.TurnOn => true,
            OverrideAction.TurnOff => false,
            _ => !current
        };


        private CommandResultDto Refused(string message) => new(false, message, null, History());


        /// <summary>
        /// "Valve 1 → CLOSED, Valve 2 → CLOSED" - in each device's own vocabulary.
        /// </summary>
        private string Describe(IReadOnlyList<DeviceChangeDto> changes, bool forward) =>
            string.Join(", ", changes.Select(change =>
            {
                bool state = forward ? change.Next : change.Previous;
                string label = registry.TryGetSnapshot(change.DeviceId, out var device) ? device.Category.StateLabel(state) : (state ? "ON" : "OFF");
                return $"{change.DisplayName} → {label}";
            }));


        private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";


        private static CommandRecordDto ToDto(CommandRecord record) => new(
            record.Id,
            record.Label,
            record.Action,
            record.Scope,
            record.Changes,
            record.IssuedAtUtc,
            record.Origin);
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
