using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RateLimiter.Configuration;
using RateLimiter.Core;
using System.Net;

namespace RateLimiter.Tests.Unit;

public class SlidingWindowRateLimiterTests
{
    private static SlidingWindowRL CreateLimiter(int maxRequests = 5, int windowSizeSeconds = 60)
    {
        var options = Options.Create(new RateLimiterOptions
        {
            MaxRequests = maxRequests,
            WindowSizeSeconds = windowSizeSeconds
        });

        return new SlidingWindowRL(options, NullLogger<SlidingWindowRL>.Instance);
    }

    [Fact]
    public async Task PermiteRequestsPorDebajoDelLimite()
    {
        var limiter = CreateLimiter(maxRequests: 5);

        for (int i = 0; i < 5; i++)
        {
            var result = await limiter.IsAllowedAsync("cliente1", "/Test");
            Assert.True(result, $"El request {i + 1} deberia ser permitido");
        }
    }

    [Fact]
    public async Task BloqueaRequestsAlSuperarElLimite()
    {
        var limiter = CreateLimiter(maxRequests: 5);

        for (int i = 0; i < 5; i++)
            await limiter.IsAllowedAsync("cliente1", "/Test");

        var result = await limiter.IsAllowedAsync("cliente1", "/Test");
        Assert.False(result, "El request 6 debería ser bloqueado");
    }

    [Fact]
    public async Task ClientesIndependientesNoSeAfectan()
    {
        var limiter = CreateLimiter(maxRequests: 3);

        for (int i = 0; i < 3; i++)
            await limiter.IsAllowedAsync("cliente1", "/Test");

        var cliente1Bloqueado = await limiter.IsAllowedAsync("cliente1", "/Test");
        Assert.False(cliente1Bloqueado, "cliente1 debería estar bloqueado");

        var cliente2Permitido = await limiter.IsAllowedAsync("cliente2", "/Test");
        Assert.True(cliente2Permitido, "cliente2 debería ser independiente de cliente1");
    }

    [Fact]
    public async Task PermiteExactamenteElRequestDelLimite()
    {
        var limiter = CreateLimiter(maxRequests: 10);

        bool lastResult = false;
        for (int i = 0; i < 10; i++)
            lastResult = await limiter.IsAllowedAsync("cliente1", "/Test");

        Assert.True(lastResult, "El request número exacto del límite debería ser permitido");
    }

    [Fact]
    public async Task MultipleClientesConcurrentesNoSeInterrumpen()
    {
        var limiter = CreateLimiter(maxRequests: 100);

        var tasks = Enumerable.Range(0, 50)
            .Select(_ => limiter.IsAllowedAsync("clienteConcurrente", "/Test"));

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.True(r));
    }

    [Fact]
    public async Task LimpiezaEliminaVentanasExpiradas()
    {
        var limiter = CreateLimiter(maxRequests: 10, windowSizeSeconds: 60);

        // Genero tráfico para poblar el diccionario
        await limiter.IsAllowedAsync("cliente1", "/Test");
        await limiter.IsAllowedAsync("cliente2", "/Test");

        // Llamo la limpieza directamente via reflexión porque es un método privado
        var cleanupMethod = typeof(SlidingWindowRL)
            .GetMethod("CleanupExpiredWindows",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);

        cleanupMethod!.Invoke(limiter, [null]);

        // Después de la limpieza el limiter sigue funcionando correctamente
        var result = await limiter.IsAllowedAsync("cliente1", "/Test");
        Assert.True(result, "El limiter debe seguir funcionando después de la limpieza");
    }

    [Fact]
    public async Task EndpointsDistintosLimitesIndependientes()
    {
        var options = Options.Create(new RateLimiterOptions
        {
            MaxRequests = 10,
            WindowSizeSeconds = 60,
            EndpointLimits = new Dictionary<string, int>
            {
                { "/Test/ordenes", 3 },
                { "/Test/cotizaciones", 15 }
            }
        });

        var limiter = new SlidingWindowRL(options, NullLogger<SlidingWindowRL>.Instance);

        for (int i = 0; i < 3; i++)
            await limiter.IsAllowedAsync("cliente1", "/Test/ordenes");

        var ordenesBloqueado = await limiter.IsAllowedAsync("cliente1", "/Test/ordenes");
        Assert.False(ordenesBloqueado, "ordenes debería estar bloqueado después de 3 requests");

        var cotizacionesPermitido = await limiter.IsAllowedAsync("cliente1", "/Test/cotizaciones");
        Assert.True(cotizacionesPermitido, "cotizaciones debería tener su propio límite independiente");
    }

    [Fact]
    public async Task EndpointConLimiteEspecificoIgnoraElLimiteGlobal()
    {
        var options = Options.Create(new RateLimiterOptions
        {
            MaxRequests = 10,
            WindowSizeSeconds = 60,
            EndpointLimits = new Dictionary<string, int>
        {
            { "/Test/ordenes", 3 },
            { "/Test/cotizaciones", 15 }
        }
        });

        var limiter = new SlidingWindowRL(options, NullLogger<SlidingWindowRL>.Instance);

        // ordenes se bloquea en 3, no en 10
        for (int i = 0; i < 3; i++)
            await limiter.IsAllowedAsync("cliente1", "/Test/ordenes");

        var ordenesBloqueado = await limiter.IsAllowedAsync("cliente1", "/Test/ordenes");
        Assert.False(ordenesBloqueado, "ordenes debería bloquearse en 3, no en el límite global de 10");
    }

    [Fact]
    public async Task EndpointSinConfiguracionUsaLimiteGlobal()
    {
        var options = Options.Create(new RateLimiterOptions
        {
            MaxRequests = 5,
            WindowSizeSeconds = 60,
            EndpointLimits = new Dictionary<string, int>
        {
            { "/Test/ordenes", 3 }
        }
        });

        var limiter = new SlidingWindowRL(options, NullLogger<SlidingWindowRL>.Instance);

        // /Test no tiene límite específico, usa el global de 5
        for (int i = 0; i < 5; i++)
            await limiter.IsAllowedAsync("cliente1", "/Test");

        var result = await limiter.IsAllowedAsync("cliente1", "/Test");
        Assert.False(result, "/Test debería bloquearse en el límite global de 5");
    }
}