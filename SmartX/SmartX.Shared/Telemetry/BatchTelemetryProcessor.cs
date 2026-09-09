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
        public static List<TelemetryPacket<float>> ProcessRawBatches(string sensorId, float[][] rawBatches, string unit)
        {
            var packets = new List<TelemetryPacket<float>>();

            // Walks through each bach of raw reading, one at a time
            for (int batchIndex = 0; batchIndex < rawBatches.Length; batchIndex++)
            {

                // Grabs the raw reading for the current batch
                float[] batch = rawBatches[batchIndex];

                // Works out what timestamp this batch should start at (based on which batch it is and how many readings it has)
                var batchStart = DateTimeOffset.UtcNow.AddSeconds(-(rawBatches.Length - batchIndex) * batch.Length);

                // Pairs each raw reading in the batch with sensorId, unit, and a timestamp
                // Adds the finished packet to the list
                for (int sampleIndex = 0; sampleIndex < batch.Length; sampleIndex++)
                {
                    packets.Add(new TelemetryPacket<float>(
                    sensorId,
                    batch[sampleIndex],
                    unit,
                    batchStart.AddSeconds(sampleIndex)));
                }
            }

            // Hands back the full timestamp list
            return packets;
        }

        //----------------------------------------------------------//

    }
}

//------------------------------------------- End Of File -------------------------------------------//
