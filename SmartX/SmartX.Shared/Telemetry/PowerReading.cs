using System;
using System.Collections.Generic;
using System.Text;



namespace SmartX.Shared.Telemetry
{
    /// <summary>
    /// 
    /// </summary>
    internal class PowerReading
    {
        public string MeterId { get; }
        public double Watts { get; }
        public DateTimeOffset TimestampUtc { get; }

        public PowerReading(string meterId, double watts, DateTimeOffset? timestampUtc = null)
        {
            MeterId = meterId;
            Watts = watts;
            TimestampUtc = timestampUtc ?? DateTimeOffset.UtcNow;
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
