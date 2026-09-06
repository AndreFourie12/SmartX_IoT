using System;
using System.Collections.Generic;
using System.Text;

namespace SmartX.Shared.Telemetry
{
    /// <summary>
    /// Non-generic surface shared by every TelemetryPacket
    /// Allows the API and dashboard to acces differnt types (floats, ints, bools)
    /// through one uniform reference type.
    /// </summary>
  
    public interface ITelemetryPacket
    {
        string SensorId { get; }
        DateTimeOffset TimestampUtc { get; }
        string Unit { get; }

        // String projection of the strongly-typed value, formatted without boxing.
        string ValueAsString { get; }
    }
}

//------------------------------------------- End Of File -------------------------------------------//