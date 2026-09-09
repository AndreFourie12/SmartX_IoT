using System;
using System.Collections.Generic;
using System.Text;

namespace SmartX.Shared.Telemetry
{
    /// <summary>
    /// Generic wrapper for a single telemetry reading
    /// Allows pipeline to handle all types uniformly while 
    /// keeping a genuine value type all the way through storage and processing
    /// </summary>
    
    public sealed class TelemetryPacket<T> : ITelemetryPacket where T : struct
    {
        public  T Value { get; }
        public  DateTimeOffset TimestampUtc { get;  }
        public  string Unit { get; }
        public string SensorId { get;  }

        public TelemetryPacket(string sensorId, T value, string unit, DateTimeOffset? timestampUtc = null)
        {
            Value = value;
            TimestampUtc = timestampUtc ?? DateTimeOffset.UtcNow;
            Unit = unit;
            SensorId = sensorId;
        }
        //----------------------------------------------------------//



        public string ValueAsString => Value is IFormattable formattable
        ? formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture)
        : Value.ToString() ?? string.Empty;

        public override string ToString() => $"[{TimestampUtc:HH:mm:ss}] {SensorId} = {ValueAsString}{Unit}";
    }
}

//------------------------------------------- End Of File -------------------------------------------//
