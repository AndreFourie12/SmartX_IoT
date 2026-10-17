namespace SmartX.Shared.Models
{
    /// <summary>
    /// Operational category attached to each sensor, describing what the device does.
    /// Part 2 adds the hydroponics devices (soil moisture probes, water pumps, valves) and a
    /// temperature probe so the command stream has real actuators to drive and real thresholds to watch.
    /// SensorCategoryInfo holds the data type, unit and thresholds that go with each one.
    /// </summary>
    public enum SensorCategory
    {
        Environmental,
        PowerConsumption,
        Actuator,
        SoilMoisture,
        Temperature,
        WaterPump,
        Valve
    }
}

//------------------------------------------- End Of File -------------------------------------------//
