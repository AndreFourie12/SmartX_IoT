namespace SmartX.Api.Streaming
{
    /// <summary>
    /// Key for every SortedDictionary timeline in the gateway.
    ///
    /// A bare timestamp is not a safe key: two packets from one device can carry the same timestamp
    /// (a batch flushed at one-second resolution, two sensors sampled in the same tick), and a
    /// SortedDictionary would either throw on the duplicate Add or silently overwrite the first
    /// reading. Pairing the timestamp with the gateway's monotonic ingest sequence makes every key
    /// unique while still ordering by time first - equal timestamps fall back to arrival order.
    /// </summary>
    public readonly record struct TimelineKey(DateTimeOffset TimestampUtc, long Sequence) : IComparable<TimelineKey>
    {
        public int CompareTo(TimelineKey other)
        {
            int byTime = TimestampUtc.CompareTo(other.TimestampUtc);
            return byTime != 0 ? byTime : Sequence.CompareTo(other.Sequence);
        }
    }
}

//------------------------------------------- End Of File -------------------------------------------//
