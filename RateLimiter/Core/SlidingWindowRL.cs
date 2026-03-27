using Microsoft.Extensions.Options;
using RateLimiter.Configuration;
using System.Collections.Concurrent;

namespace RateLimiter.Core;

public class SlidingWindowRL : IRateLimiter , IDisposable
{
    private readonly RateLimiterOptions _options;
    private readonly ILogger<SlidingWindowRL> _logger;
    private readonly Timer _cleanUpTimer;

    private readonly ConcurrentDictionary<string, int> _counters = new();
    public SlidingWindowRL(
        IOptions<RateLimiterOptions> options,
        ILogger<SlidingWindowRL> logger)
    {
        _options = options.Value;
        _logger = logger;
        // La IA me sugirió usar un Timer para limpiar ventanas viejas y evitar
        // que el diccionario crezca indefinidamente. Los dos últimos parámetros son:
        // - primer TimeSpan: cuánto espera antes del primer disparo
        // - segundo TimeSpan: cada cuánto se repite
        // Uso windowSize en ambos porque es el intervalo natural de expiración.
        _cleanUpTimer = new Timer(
            CleanupExpiredWindows, 
            null, 
            TimeSpan.FromSeconds(_options.WindowSizeSeconds), 
            TimeSpan.FromSeconds(_options.WindowSizeSeconds)
            );
    }

    public Task<bool> IsAllowedAsync(string clientId, string path, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var windowSize = _options.WindowSizeSeconds;

        // Si el endpoint tiene un límite específico configurado lo uso,
        // si no uso el límite global como fallback
        var limit = _options.EndpointLimits.TryGetValue(path, out var endpointLimit)
            ? endpointLimit
            : _options.MaxRequests;

        var currentWindow = now.ToUnixTimeSeconds() / windowSize;
        var previousWindow = currentWindow - 1;

        var currentKey = $"{clientId}:{path}:{currentWindow}";
        var previousKey = $"{clientId}:{path}:{previousWindow}";

        // AddOrUpdate es atómico — lo usé en lugar de un get + set separados
        // para evitar race conditions. La sugerencia de usar este método la tomé de la IA,
        // pero entiendo que ConcurrentDictionary garantiza que dos threads no pisen el mismo contador al mismo tiempo
        var currentCount = _counters.AddOrUpdate(currentKey, 1, (_, count) => count + 1);
        var previousCount = _counters.GetValueOrDefault(previousKey, 0);

        var elapsedInWindow = now.ToUnixTimeSeconds() % windowSize;
        var previousWeight = 1.0 - ((double)elapsedInWindow / windowSize);

        // Fórmula del sliding window — la IA me ayudó a traducir el concepto matemático
        // a código. Entiendo que: cuanto más avanzada está la ventana actual,
        // menos peso tiene la anterior en el cálculo total.
        var estimated = previousCount * previousWeight + currentCount;

        var allowed = estimated <= limit;

        if (!allowed)
            _logger.LogWarning("Rate limit excedido para {ClientId} en {Path}. Límite: {Limit}. Estimado: {Estimated}", clientId, path, limit, estimated);

        return Task.FromResult(allowed);
    }

    //elimino las ventanas que ya expiraron para evitar que el diccionario crezca sin control
    //conservo la ventana actual y la anterior porque ambas se usan en el calculo.
    private void CleanupExpiredWindows(object? state)
    { 
        var windowSize = _options.WindowSizeSeconds;
        var currentWindow = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / windowSize;
        var minimumValidWindow = currentWindow - 1;

        foreach (var key in _counters.Keys)
        {
            var parts = key.Split(':');
            if (parts.Length >= 2 && long.TryParse(parts[^1], out var windowIndex))
            {
                if (windowIndex < minimumValidWindow)
                    _counters.TryRemove(key, out _);
            }
        }
        _logger.LogInformation("Limpieza de ventanas expiradas completada");
    }
    public void Dispose()
    {
        _cleanUpTimer.Dispose();
    }
}