using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;



namespace SmartX.Shared.Telemetry
{
    /// <summary>
    /// A single smart-meter wattage sample
    /// Operators let call sites read like the electrical equations they represent
    /// e.g.  combining two meters on the same  circuit with Meter3 = Meter1 + Meter2
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



        /// <summary>
        /// Aggregates the load of two meters into a combined virtual reading.
        /// </summary>
        public static PowerReading operator +(PowerReading a, PowerReading b) =>
            new($"{a.MeterId}+{b.MeterId}", a.Watts + b.Watts);
        //----------------------------------------------------------//


        /// <summary>
        /// Difference between two PowerReading object
        /// Basis for spike/drop anomoly checks
        /// </summary>
        public static PowerReading operator -(PowerReading a, PowerReading b)
            => new($"{a.MeterId}-{b.MeterId}", a.Watts - b.Watts);
        //----------------------------------------------------------//


        /// <summary>
        /// Checks if one reading has more watts than another.
        /// </summary>
        public static bool operator > (PowerReading a, PowerReading b) 
            => a.Watts > b.Watts;
        //----------------------------------------------------------//


        /// <summary>
        /// Checks if one reading has fewer watts than another.
        /// </summary>
        public static bool operator <(PowerReading a, PowerReading b)
            =>  a.Watts < b.Watts;
        //----------------------------------------------------------//


        /// <summary>
        /// Checks if one reading has greater than or equal watts.
        /// </summary>
        public static bool operator >=(PowerReading a, PowerReading b) 
            => a.Watts >= b.Watts;
        //----------------------------------------------------------//


        /// <summary>
        /// Checks if one reading has less than or equal watts.
        /// </summary>
        public static bool operator <=(PowerReading a, PowerReading b) 
            =>  a.Watts <= b.Watts;
        //----------------------------------------------------------//


        /// <summary>
        /// Checks if two readings have the same meter ID and wattage.
        /// </summary>
        public static bool operator ==(PowerReading a, PowerReading b) 
            => a.Equals(b);
        //----------------------------------------------------------//


        /// <summary>
        /// Checks if two readings are different.
        /// </summary>
        public static bool operator !=(PowerReading a, PowerReading b) 
            => !a.Equals(b);
        //----------------------------------------------------------//


        /// <summary>
        /// Determines whether two PowerReading objects are equa
        /// </summary>
        public bool Equals(PowerReading other)
            => MeterId == other.MeterId && Watts.Equals(other.Watts);
        //----------------------------------------------------------//


        /// <summary>
        /// Determines whether two PowerReading objects are equal
        /// </summary>
        public override bool Equals(object? obj) 
            => obj is PowerReading other && Equals(other);
        //----------------------------------------------------------//


        /// <summary>
        /// Creates a hash balue based on the meterId and watts
        /// </summary>
        public override int GetHashCode()
            => HashCode.Combine(MeterId, Watts);
        //----------------------------------------------------------//


        /// <summary>
        /// Compares two readings by their wattage for sorting
        /// </summary>
        public int CompareTo(PowerReading other)
            => Watts.CompareTo(other.Watts);
        //----------------------------------------------------------//


        /// <summary>
        /// Converts the reading into a readable format
        /// </summary>
        public override string ToString()
            => $"{MeterId}: {Watts:F1}W";
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
