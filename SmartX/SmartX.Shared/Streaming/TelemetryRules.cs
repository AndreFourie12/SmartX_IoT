using SmartX.Shared.Models;
using static System.FormattableString;

namespace SmartX.Shared.Streaming
{
    /// <summary>
    /// The verdict on one reading that broke a threshold.
    /// </summary>
    public readonly record struct FaultAssessment(FaultKind Kind, AlertSeverity Severity, string Description);
    //----------------------------------------------------------//


    /// <summary>
    /// Warning and critical limits for a category, low and high side. Null means that side has no limit.
    /// The client draws these as bands on the timeline chart.
    /// </summary>
    public sealed record ThresholdBand(double? CriticalLow, double? WarningLow, double? WarningHigh, double? CriticalHigh);
    //----------------------------------------------------------//


    /// <summary>
    /// Classifies a reading the instant it reaches the API, before it is queued. This is what
    /// decides whether a packet waits in the FIFO routine queue or bypasses it through the
    /// priority alert lane, so it has to be cheap: a single switch on the category, O(1), no allocation
    /// on the healthy path.
    /// </summary>
    public static class TelemetryRules
    {
        public const double PowerWarningWatts = 250;
        public const double PowerCriticalWatts = 400;

        public const double MoistureWarningPercent = 30;
        public const double MoistureCriticalPercent = 20;
        public const double OverwateringPercent = 90;

        public const double HumidityLowPercent = 25;
        public const double HumidityHighPercent = 85;

        public const double TemperatureWarningCelsius = 35;
        public const double TemperatureCriticalCelsius = 40;


        /// <summary>
        /// Returns the fault a reading represents, or null when it is within range.
        /// Actuators report on/off state rather than a measurement, so they never trip a threshold.
        /// </summary>
        public static FaultAssessment? Evaluate(SensorCategory category, double value) => category switch
        {
            SensorCategory.PowerConsumption when value >= PowerCriticalWatts =>
                new(FaultKind.PowerSpike, AlertSeverity.Critical, Invariant($"Severe power spike: {value:F0} W (limit {PowerCriticalWatts:F0} W)")),
            SensorCategory.PowerConsumption when value >= PowerWarningWatts =>
                new(FaultKind.PowerSpike, AlertSeverity.Warning, Invariant($"High load: {value:F0} W (warning at {PowerWarningWatts:F0} W)")),

            SensorCategory.SoilMoisture when value < MoistureCriticalPercent =>
                new(FaultKind.MoistureDrop, AlertSeverity.Critical, Invariant($"Critical moisture drop: {Below(value):F1}% (below {MoistureCriticalPercent:F0}%)")),
            SensorCategory.SoilMoisture when value < MoistureWarningPercent =>
                new(FaultKind.MoistureDrop, AlertSeverity.Warning, Invariant($"Moisture low: {Below(value):F1}% (below {MoistureWarningPercent:F0}%)")),
            SensorCategory.SoilMoisture when value > OverwateringPercent =>
                new(FaultKind.Overwatering, AlertSeverity.Warning, Invariant($"Overwatering: {Above(value):F1}% (above {OverwateringPercent:F0}%)")),

            SensorCategory.Environmental when value < HumidityLowPercent || value > HumidityHighPercent =>
                new(FaultKind.HumidityOutOfRange, AlertSeverity.Warning, Invariant($"Humidity out of range: {value:F1}%")),

            SensorCategory.Temperature when value >= TemperatureCriticalCelsius =>
                new(FaultKind.Overheating, AlertSeverity.Critical, Invariant($"Overheating: {Above(value):F1} °C (limit {TemperatureCriticalCelsius:F0} °C)")),
            SensorCategory.Temperature when value >= TemperatureWarningCelsius =>
                new(FaultKind.Overheating, AlertSeverity.Warning, Invariant($"Running hot: {Above(value):F1} °C (warning at {TemperatureWarningCelsius:F0} °C)")),

            _ => null
        };
        //----------------------------------------------------------//


        /// <summary>
        /// Rounds towards the limit that was crossed, so 29.96% reads "29.9% (below 30%)" rather than the
        /// contradictory "30.0% (below 30%)".
        /// </summary>
        private static double Below(double value) => Math.Floor(value * 10) / 10;
        private static double Above(double value) => Math.Ceiling(value * 10) / 10;
        //----------------------------------------------------------//


        /// <summary>
        /// The limits for a category, for drawing threshold bands and for trend forecasting.
        /// </summary>
        public static ThresholdBand? BandFor(SensorCategory category) => category switch
        {
            SensorCategory.PowerConsumption => new(null, null, PowerWarningWatts, PowerCriticalWatts),
            SensorCategory.SoilMoisture => new(MoistureCriticalPercent, MoistureWarningPercent, OverwateringPercent, null),
            SensorCategory.Environmental => new(null, HumidityLowPercent, HumidityHighPercent, null),
            SensorCategory.Temperature => new(null, null, TemperatureWarningCelsius, TemperatureCriticalCelsius),
            _ => null
        };
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
