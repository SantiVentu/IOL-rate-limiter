namespace RateLimiter.Configuration
 
{
    public class RateLimiterOptions
    {
        public const string SectionName = "RateLimiter";
        public int MaxRequests { get; init; } = 10;
        public int WindowSizeSeconds { get; init; } = 60;
        public Dictionary<string, int> EndpointLimits { get; init; } = new();

    }
}
