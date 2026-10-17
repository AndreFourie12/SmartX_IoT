using SmartX.Shared.Models;
using SmartX.Shared.Streaming;

namespace SmartX.Api.Commands
{
    /// <summary>
    /// Operator-facing wording for override commands, shared by the command history and the
    /// suggestion engine so a suggested action and the command it runs are described identically.
    /// </summary>
    public static class CommandLabels
    {
        /// <summary>
        /// "Turn ON Pump A", "Close Valve 2", "Shut down all valves", and so on.
        /// </summary>
        public static string For(OverrideAction action, CommandScope scope, string? displayName = null, SensorCategory? category = null)
        {
            if (scope == CommandScope.Device)
                return $"{Verb(action, category)} {displayName}";

            return (scope, action) switch
            {
                (CommandScope.AllValves, OverrideAction.TurnOff) => "Shut down all valves",
                (CommandScope.AllValves, OverrideAction.TurnOn) => "Open all valves",
                (CommandScope.AllPumps, OverrideAction.TurnOff) => "Stop all pumps",
                (CommandScope.AllPumps, OverrideAction.TurnOn) => "Start all pumps",
                (CommandScope.AllRelays, OverrideAction.TurnOff) => "Switch all relays OFF",
                (CommandScope.AllRelays, OverrideAction.TurnOn) => "Switch all relays ON",
                (CommandScope.AllActuators, OverrideAction.TurnOff) => "Emergency stop (all actuators OFF)",
                (CommandScope.AllActuators, OverrideAction.TurnOn) => "Switch every actuator ON",
                (_, OverrideAction.Toggle) => $"Toggle {GroupNoun(scope)}",
                _ => $"{action} {GroupNoun(scope)}"
            };
        }
        //----------------------------------------------------------//


        /// <summary>
        /// The verb for one device: valves open and close, everything else switches on and off.
        /// </summary>
        public static string Verb(OverrideAction action, SensorCategory? category) => (action, category) switch
        {
            (OverrideAction.TurnOn, SensorCategory.Valve) => "Open",
            (OverrideAction.TurnOff, SensorCategory.Valve) => "Close",
            (OverrideAction.TurnOn, _) => "Turn ON",
            (OverrideAction.TurnOff, _) => "Turn OFF",
            _ => "Toggle"
        };
        //----------------------------------------------------------//


        private static string GroupNoun(CommandScope scope) => scope switch
        {
            CommandScope.AllValves => "all valves",
            CommandScope.AllPumps => "all pumps",
            CommandScope.AllRelays => "all relays",
            _ => "all actuators"
        };
        //----------------------------------------------------------//


        /// <summary>
        /// The categories a group command reaches.
        /// </summary>
        public static SensorCategory[] CategoriesFor(CommandScope scope) => scope switch
        {
            CommandScope.AllValves => [SensorCategory.Valve],
            CommandScope.AllPumps => [SensorCategory.WaterPump],
            CommandScope.AllRelays => [SensorCategory.Actuator],
            CommandScope.AllActuators => [SensorCategory.Valve, SensorCategory.WaterPump, SensorCategory.Actuator],
            _ => []
        };
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
