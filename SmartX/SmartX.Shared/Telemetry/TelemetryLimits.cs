namespace SmartX.Shared.Telemetry
{
    /// <summary>
    /// Limits the gateway and client both need to agree on.
    /// SensorHistoryCapacity is the size of each sensor's ring buffer: once it holds this many readings,
    /// every new one rolls the oldest out, so the reading count stops growing.
    /// </summary>
    public static class TelemetryLimits
    {
        public const int SensorHistoryCapacity = 500;
    }
    //----------------------------------------------------------//
}
//------------------------------------------- End Of File -------------------------------------------//
