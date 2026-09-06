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
    
    public class TelemetryPacket<T> where T : struct
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
    }
}

//------------------------------------------- End Of File -------------------------------------------//
