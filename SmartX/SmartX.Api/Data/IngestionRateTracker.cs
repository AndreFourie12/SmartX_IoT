namespace SmartX.Api.Data
{
    /// <summary>
    /// 
    /// </summary>
    public sealed class IngestionRateTracker
    {
        private const int WindowSeconds = 10;

        private readonly long[] _bucketSecond = new long[WindowSeconds];
        private readonly int[] _bucketCount = new int[WindowSeconds];
        private readonly Lock _gate = new();


        /// <summary>
        /// 
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
        /// 
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