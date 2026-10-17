namespace SmartX.Shared.Telemetry
{
    /// <summary>
    /// Non-generic surface shared by every TelemetryPacket
    /// Allows the API and dashboard to acces differnt types (floats, ints, bools)
    /// through one uniform reference type.
    /// </summary>

    public interface ITelemetryPacket
    {
        // ID of the sensor the reading came from
        string SensorId { get; }

        // Time the reading came through
        DateTimeOffset TimestampUtc { get; }

        // What unit is attached to that reading
        string Unit { get; }

        // String projection of the strongly-typed value, formatted without boxing.
        string ValueAsString { get; }

        // The value as a double for threshold checks, charts and statistics (bool reads as 1 or 0).
        // NaN for a type with no numeric meaning.
        double NumericValue { get; }
    }
}

//------------------------------------------- End Of File -------------------------------------------//
