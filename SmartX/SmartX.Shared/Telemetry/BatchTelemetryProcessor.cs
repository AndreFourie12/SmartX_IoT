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
            // Size the list once up front - the jagged shape tells us exactly how many packets are coming,
            // so the List never has to grow and copy its backing array mid-batch.
            int totalSamples = 0;
            foreach (T[] window in rawBatches)
                totalSamples += window.Length;

            var packets = new List<TelemetryPacket<T>>(totalSamples);

            // The batch is one second per sample, ending now. Counting samples across the whole batch
            // (rather than per window) keeps the windows back to back: Part 1 derived each window's
            // start from its own length, so a 5-sample window and a 2-sample window could overlap in time.
            var batchStart = DateTimeOffset.UtcNow.AddSeconds(-totalSamples);
            int sampleOffset = 0;

            // Walks through each bach of raw reading, one at a time
            for (int windowIndex = 0; windowIndex < rawBatches.Length; windowIndex++)
            {

                // Grabs the raw reading for the current batch
                T[] window = rawBatches[windowIndex];

                // Pairs each raw reading in the batch with sensorId, unit, and a timestamp
                // Adds the finished packet to the list
                for (int sampleIndex = 0; sampleIndex < window.Length; sampleIndex++)
                {
                    packets.Add(new TelemetryPacket<T>(
                    sensorId,
                    window[sampleIndex],
                    unit,
                    batchStart.AddSeconds(sampleOffset++)));
                }
            }

            // Hands back the full timestamp list
            return packets;
        }

        //----------------------------------------------------------//

    }
}

//------------------------------------------- End Of File -------------------------------------------//
