using System.Globalization;
using System.Runtime.CompilerServices;

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



        /// <summary>
        /// Formats the value without boxing it.
        /// Part 1 tested "Value is IFormattable", which boxes T onto the heap just to ask the question.
        /// typeof(T) == typeof(float) is a constant the JIT folds away when it compiles each closed
        /// generic, so TelemetryPacket&lt;float&gt; ends up with only the float branch, and Unsafe.As
        /// reinterprets the value in place instead of copying it into an object.
        /// </summary>
        public string ValueAsString
        {
            get
            {
                T value = Value;

                if (typeof(T) == typeof(float))
                    return Unsafe.As<T, float>(ref value).ToString(CultureInfo.InvariantCulture);

                if (typeof(T) == typeof(int))
                    return Unsafe.As<T, int>(ref value).ToString(CultureInfo.InvariantCulture);

                if (typeof(T) == typeof(double))
                    return Unsafe.As<T, double>(ref value).ToString(CultureInfo.InvariantCulture);

                if (typeof(T) == typeof(bool))
                    return Unsafe.As<T, bool>(ref value) ? "True" : "False";

                // Any other struct: a constrained call, which also avoids boxing.
                return value.ToString() ?? string.Empty;
            }
        }
        //----------------------------------------------------------//



        /// <summary>
        /// The value as a double for threshold checks and charting, using the same box-free branches.
        /// </summary>
        public double NumericValue
        {
            get
            {
                T value = Value;

                if (typeof(T) == typeof(float))
                    return Unsafe.As<T, float>(ref value);

                if (typeof(T) == typeof(int))
                    return Unsafe.As<T, int>(ref value);

                if (typeof(T) == typeof(double))
                    return Unsafe.As<T, double>(ref value);

                if (typeof(T) == typeof(bool))
                    return Unsafe.As<T, bool>(ref value) ? 1 : 0;

                return double.NaN;
            }
        }
        //----------------------------------------------------------//

        public override string ToString() => $"[{TimestampUtc:HH:mm:ss}] {SensorId} = {ValueAsString}{Unit}";
    }
}

//------------------------------------------- End Of File -------------------------------------------//
