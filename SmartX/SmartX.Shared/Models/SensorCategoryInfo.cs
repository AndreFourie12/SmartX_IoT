namespace SmartX.Shared.Models
{
    /// <summary>
    /// Everything that follows from a sensor's category, kept in one place so the API, the
    /// simulator and the WPF client can never disagree about it. Part 1 repeated this mapping
    /// inline (Environmental -> float, Power -> int, anything else -> bool), which would have
    /// silently treated every new category as a bool.
    /// </summary>
    public static class SensorCategoryInfo
    {
        /// <summary>
        /// The wire type a device of this category reports in.
        /// </summary>
        public static TelemetryDataType DataType(this SensorCategory category) => category switch
        {
            SensorCategory.Environmental or SensorCategory.SoilMoisture or SensorCategory.Temperature => TelemetryDataType.Float,
            SensorCategory.PowerConsumption => TelemetryDataType.Int,
            _ => TelemetryDataType.Bool
        };
        //----------------------------------------------------------//


        /// <summary>
        /// The unit that goes with a reading of this category.
        /// </summary>
        public static string Unit(this SensorCategory category) => category switch
        {
            SensorCategory.Environmental or SensorCategory.SoilMoisture => "%",
            SensorCategory.Temperature => "°C",
            SensorCategory.PowerConsumption => "W",
            _ => string.Empty
        };
        //----------------------------------------------------------//


        /// <summary>
        /// True for devices that accept manual override commands (pumps, valves, relays).
        /// </summary>
        public static bool IsActuator(this SensorCategory category) =>
            category is SensorCategory.Actuator or SensorCategory.WaterPump or SensorCategory.Valve;
        //----------------------------------------------------------//


        /// <summary>
        /// Human readable name for filter chips and labels.
        /// </summary>
        public static string DisplayName(this SensorCategory category) => category switch
        {
            SensorCategory.Environmental => "Environmental",
            SensorCategory.PowerConsumption => "Power",
            SensorCategory.Actuator => "Smart Relay",
            SensorCategory.SoilMoisture => "Soil Moisture",
            SensorCategory.Temperature => "Temperature",
            SensorCategory.WaterPump => "Water Pump",
            SensorCategory.Valve => "Valve",
            _ => category.ToString()
        };
        //----------------------------------------------------------//


        /// <summary>
        /// What "on" and "off" mean for an actuator, so a valve reads OPEN/CLOSED rather than ON/OFF.
        /// </summary>
        public static string StateLabel(this SensorCategory category, bool isOn) => category switch
        {
            SensorCategory.Valve => isOn ? "OPEN" : "CLOSED",
            _ => isOn ? "ON" : "OFF"
        };
        //----------------------------------------------------------//


        /// <summary>
        /// The band a healthy device of this category normally reports within.
        /// Used to generate believable simulated readings on both sides of the API.
        /// </summary>
        public static (double Min, double Max) NormalRange(this SensorCategory category) => category switch
        {
            SensorCategory.Environmental => (40, 70),
            SensorCategory.SoilMoisture => (35, 70),
            SensorCategory.Temperature => (19, 30),
            SensorCategory.PowerConsumption => (125, 175),
            _ => (0, 1)
        };
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
