using System;
using System.Collections.Generic;
using System.Text;

namespace SmartX.Shared.Telemetry
{
    /// <summary>
    /// Converts a raw batch upload into typed packets.
    /// Device buffers several sampling batches and flushed them together.
    /// Wire shape is a naturally jagged array.
    /// The raw, unstructered batch is only ever held in the array, 
    /// very consumer downstream works against the optomised list of telemetryPackets this method produces
    /// </summary>
    public static class BatchTelemetryProcessor
    {
        public static List<TelemetryPacket<T>> ProcessRawBatches<T>(string sensorId, T[][] rawBatches, string unit) where T : struct
        {
            var packets = new List<TelemetryPacket<T>>();

            // Walks through each bach of raw reading, one at a time
            for (int windowIndex = 0; windowIndex < rawBatches.Length; windowIndex++)
            {

                // Grabs the raw reading for the current batch
                T[] window = rawBatches[windowIndex];

                // Works out what timestamp this batch should start at (based on which batch it is and how many readings it has)
                var windowStart = DateTimeOffset.UtcNow.AddSeconds(-(rawBatches.Length - windowIndex) * window.Length);

                // Pairs each raw reading in the batch with sensorId, unit, and a timestamp
                // Adds the finished packet to the list
                for (int sampleIndex = 0; sampleIndex < window.Length; sampleIndex++)
                {
                    packets.Add(new TelemetryPacket<T>(
                    sensorId,
                    window[sampleIndex],
                    unit,
                    windowStart.AddSeconds(sampleIndex)));
                }
            }

            // Hands back the full timestamp list
            return packets;
        }

        //----------------------------------------------------------//

    }
}

//------------------------------------------- End Of File -------------------------------------------//
