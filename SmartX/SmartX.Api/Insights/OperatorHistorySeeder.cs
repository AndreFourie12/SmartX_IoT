using SmartX.Shared.Contracts;
using SmartX.Shared.Streaming;

namespace SmartX.Api.Insights
{
    /// <summary>
    /// Replays a few days of mock operator history into the engine at startup, the same way the
    /// simulator seeds telemetry history - so "Suggested Actions" has patterns to draw on the first
    /// time the dashboard opens, instead of needing someone to run through each scenario first.
    ///
    /// The episodes go through exactly the same learning path as live actions (raise condition,
    /// operator searches, operator commands, condition clears). They are flagged as not-live, so the
    /// dashboard can show how much of a pattern was seeded and how much was learned this session.
    ///
    /// The headline pattern is the one from the brief: when Moisture Sensor B drops, the operator looks
    /// up Moisture Sensor B, then Pump A, then turns Pump A on (Pump A feeds the shared header line
    /// both hydroponic bays draw from). One episode deviates to Pump B, so the confidence is a
    /// believable 5 of 6 rather than a suspiciously perfect score.
    /// </summary>
    public static class OperatorHistorySeeder
    {
        private const string MoistureSensorA = "ESP32-SOIL-01";
        private const string MoistureSensorB = "ESP32-SOIL-02";
        private const string PumpA = "ESP32-PMP-01";
        private const string PumpB = "ESP32-PMP-02";
        private const string SmartMeter2 = "ESP32-PWR-02";
        private const string Relay2 = "ESP32-ACT-02";


        public static void Seed(ActionRecommendationEngine engine, DateTimeOffset nowUtc)
        {
            var timeline = new List<(DateTimeOffset AtUtc, Action<ActionRecommendationEngine> Apply)>();

            // Moisture Sensor B dries out -> operator checks it, looks up Pump A, switches Pump A on.
            for (int i = 0; i < 6; i++)
            {
                var start = nowUtc.AddHours(-(6 - i) * 9).AddMinutes(-17 * i);
                var condition = new ConditionKey(MoistureSensorB, FaultKind.MoistureDrop);

                timeline.Add((start, e => e.OnConditionRaised(condition, AlertSeverity.Critical, "Critical moisture drop", start)));
                timeline.Add((start.AddSeconds(25), e => e.RecordHistoricalSearch(MoistureSensorB, start.AddSeconds(25))));

                if (i == 3)
                {
                    timeline.Add((start.AddSeconds(70), e => Command(e, PumpB, "Pump B", turnOn: true, start.AddSeconds(70))));
                }
                else
                {
                    timeline.Add((start.AddSeconds(45), e => e.RecordHistoricalSearch(PumpA, start.AddSeconds(45))));
                    timeline.Add((start.AddSeconds(80), e => Command(e, PumpA, "Pump A", turnOn: true, start.AddSeconds(80))));
                }

                timeline.Add((start.AddMinutes(4), e => e.OnConditionCleared(condition)));
            }

            // Smart Meter 2 spikes -> operator sheds load by switching off Relay 2 on the same circuit.
            for (int i = 0; i < 4; i++)
            {
                var start = nowUtc.AddHours(-(4 - i) * 13).AddMinutes(-41);
                var condition = new ConditionKey(SmartMeter2, FaultKind.PowerSpike);

                timeline.Add((start, e => e.OnConditionRaised(condition, AlertSeverity.Critical, "Severe power spike", start)));

                if (i != 1)
                    timeline.Add((start.AddSeconds(30), e => Command(e, Relay2, "Relay 2", turnOn: false, start.AddSeconds(30))));

                timeline.Add((start.AddMinutes(2), e => e.OnConditionCleared(condition)));
            }

            // Moisture Sensor A overwatered -> operator stops Pump A.
            for (int i = 0; i < 3; i++)
            {
                var start = nowUtc.AddHours(-(3 - i) * 17).AddMinutes(-5);
                var condition = new ConditionKey(MoistureSensorA, FaultKind.Overwatering);

                timeline.Add((start, e => e.OnConditionRaised(condition, AlertSeverity.Warning, "Overwatering", start)));
                timeline.Add((start.AddSeconds(55), e => Command(e, PumpA, "Pump A", turnOn: false, start.AddSeconds(55))));
                timeline.Add((start.AddMinutes(3), e => e.OnConditionCleared(condition)));
            }

            // Replay in time order, exactly as the events would have reached the engine live.
            foreach (var (_, apply) in timeline.OrderBy(entry => entry.AtUtc))
                apply(engine);
        }
        //----------------------------------------------------------//


        private static void Command(ActionRecommendationEngine engine, string deviceId, string name, bool turnOn, DateTimeOffset atUtc)
        {
            var record = new CommandRecordDto(
                $"seed-{atUtc.ToUnixTimeSeconds()}",
                $"{(turnOn ? "Turn ON" : "Turn OFF")} {name}",
                turnOn ? OverrideAction.TurnOn : OverrideAction.TurnOff,
                CommandScope.Device,
                [new DeviceChangeDto(deviceId, name, !turnOn, turnOn)],
                atUtc,
                CommandOrigin.Manual);

            engine.RecordCommand(record, atUtc, live: false);
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
