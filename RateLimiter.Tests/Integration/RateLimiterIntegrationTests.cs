using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RateLimiter.Core;
using RateLimiter.Metrics;
using System.Net;

namespace RateLimiter.Tests.Integration;

public class RateLimiterIntegrationTests
{
    // Creo un cliente nuevo por cada test para evitar que los contadores de un test afecten a los demás
    private static HttpClient CreateFreshClient()
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IRateLimiter, SlidingWindowRL>();
                    services.AddSingleton<RateLimiterMetrics>();
                });
            });

        return factory.CreateClient();
    }

    [Fact]
    public async Task Returns200CuandoEstaDebajoDeLimite()
    {
        var client = CreateFreshClient();

        var response = await client.GetAsync("/Test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Returns429CuandoSuperaElLimite()
    {
        var client = CreateFreshClient();

        for (int i = 0; i < 10; i++)
            await client.GetAsync("/Test");

        var response = await client.GetAsync("/Test");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task RetryAfterHeaderPresenteCuando429()
    {
        var client = CreateFreshClient();

        for (int i = 0; i < 10; i++)
            await client.GetAsync("/Test");

        var response = await client.GetAsync("/Test");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.Contains("Retry-After"), "Debe incluir el header Retry-After");
    }

    [Fact]
    public async Task MetricasRegistradanRequestPermitidosYBloqueados()
    {
        var client = CreateFreshClient();
        for (int i = 0; i < 10; i++)
            await client.GetAsync("/Test");

        await client.GetAsync("/Test"); // Este debería ser bloqueado

        var response = await client.GetAsync("/Test/metrics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("totalRequest", body);
        Assert.Contains("allowed", body);
        Assert.Contains("blocked", body);
    }

    [Fact]
    public async Task MetricasNoCuentanComoRequestDelRateLimiter()
    {
        var client = CreateFreshClient();

        for (int i = 0; i < 10; i++)
            await client.GetAsync("/Test");

        // El endpoint de métricas no debería contar como request
        // y debe responder 200 aunque el cliente esté bloqueado
        var response = await client.GetAsync("/Test/metrics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    [Fact]
    public async Task OrdenesRespetaSuLimiteEstricto()
    {
        var client = CreateFreshClient();

        // ordenes tiene límite de 3
        for (int i = 0; i < 3; i++)
            await client.GetAsync("/Test/ordenes");

        var response = await client.GetAsync("/Test/ordenes");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task CotizacionesRespetaSuLimiteMasPermisivo()
    {
        var client = CreateFreshClient();

        // cotizaciones tiene límite de 15
        for (int i = 0; i < 15; i++)
            await client.GetAsync("/Test/cotizaciones");

        var response = await client.GetAsync("/Test/cotizaciones");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task OrdenesYCotizacionesTienenLimitesIndependientes()
    {
        var client = CreateFreshClient();

        // Bloqueo ordenes
        for (int i = 0; i < 3; i++)
            await client.GetAsync("/Test/ordenes");

        var ordenesBloqueado = await client.GetAsync("/Test/ordenes");
        Assert.Equal(HttpStatusCode.TooManyRequests, ordenesBloqueado.StatusCode);

        // Cotizaciones no debería verse afectado
        var cotizacionesPermitido = await client.GetAsync("/Test/cotizaciones");
        Assert.Equal(HttpStatusCode.OK, cotizacionesPermitido.StatusCode);
    }
}