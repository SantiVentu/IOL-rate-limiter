using RateLimiter.Core;
using RateLimiter.Metrics;

namespace RateLimiter.Middleware
{
    public class RateLimiterMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IRateLimiter _rateLimiter;
        private readonly ILogger<RateLimiterMiddleware> _logger;
        private readonly RateLimiterMetrics _metrics;

        private static readonly string[] _excludedPaths = ["/Test/metrics"];

        public RateLimiterMiddleware(
            RequestDelegate next, 
            IRateLimiter rateLimiter, 
            ILogger<RateLimiterMiddleware> logger,
            RateLimiterMetrics metrics
            )
        {
            _next = next;
            _rateLimiter = rateLimiter;
            _logger = logger;
            _metrics = metrics;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Dejo pasar sin rate limiting los endpoints de métricas
            if (_excludedPaths.Contains(context.Request.Path.Value, StringComparer.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }
            var clientId = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var path = context.Request.Path.Value ?? "/";

            try
            {
                var allowed = await _rateLimiter.IsAllowedAsync(clientId, path, context.RequestAborted);
                if (!allowed)
                {
                    _metrics.RecordBlocked();
                    _logger.LogWarning("Rate limit excedido para {ClientId} en {Path}", clientId, path);

                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.Response.Headers.RetryAfter = "10";
                    await context.Response.WriteAsync("Demasiadas request, intente más tarde");
                    return;
                }
                _metrics.RecordAllowed();
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado en el rate limiter para {ClientId}. Dejando pasar el request.", clientId);
                await _next(context);
            }
        }
    }
}
