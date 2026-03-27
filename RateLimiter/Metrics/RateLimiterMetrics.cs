namespace RateLimiter.Metrics
{
    public class RateLimiterMetrics
    {
        private long _totalAllowed;
        private long _totalBlocked;

        public void RecordAllowed() => Interlocked.Increment(ref _totalAllowed);
        public void RecordBlocked() => Interlocked.Increment(ref _totalBlocked);

        public long TotalAllowed => Interlocked.Read(ref _totalAllowed);
        public long TotalBlocked => Interlocked.Read(ref _totalBlocked);
        public long TotalRequests => TotalAllowed + TotalBlocked;
    }
}
