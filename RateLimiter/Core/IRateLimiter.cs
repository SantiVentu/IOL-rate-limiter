namespace RateLimiter.Core
{
    public interface IRateLimiter
    {
        Task<bool> IsAllowedAsync(string clientId, string path, CancellationToken cancellationToken = default);
    }
}
