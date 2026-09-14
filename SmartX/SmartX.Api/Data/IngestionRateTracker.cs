namespace SmartX.Api.Data
{
    /// <summary>
    /// Measures live ingestion throughput for the navigation bar's msg/s readout.
    /// </summary>
    public sealed class IngestionRateTracker
    {
        private const int WindowSeconds = 10;

        private readonly long[] _bucketSecond = new long[WindowSeconds];
        private readonly int[] _bucketCount = new int[WindowSeconds];
        private readonly Lock _gate = new();


        /// <summary>
        /// Logs that a number of readings just arrived, stamping them into the bucket for the current second.
        /// A bucket holding a second that has fallen outside the window is reset before it is reused.
        /// </summary>
        public void Record(int count = 1)
        {
            long second = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            int index = (int)(second % WindowSeconds);

            lock (_gate)
            {
                if (_bucketSecond[index] != second)
                {
                    _bucketSecond[index] = second;
                    _bucketCount[index] = 0;
                }

                _bucketCount[index] += count;
            }
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Adds up every bucket still inside the ten second window and averages it out.
        /// Returns the result rounded to one decimal so the nav bar can show it as msg/s.
        /// </summary>
        public double MessagesPerSecond()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long oldestValid = now - WindowSeconds + 1;

            lock (_gate)
            {
                int total = 0;

                for(int i = 0; i < WindowSeconds; i++)
                {
                    if (_bucketSecond[i] >= oldestValid)
                        total += _bucketCount[i];
                }

                return Math.Round((double)total / WindowSeconds, 1);
            }
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//