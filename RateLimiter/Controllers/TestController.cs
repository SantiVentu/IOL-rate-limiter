using Microsoft.AspNetCore.Mvc;
using RateLimiter.Metrics;

namespace RateLimiter.Controllers;

[ApiController]
[Route("[controller]")]
public class TestController : ControllerBase
{
    private readonly ILogger<TestController> _logger;
    private readonly RateLimiterMetrics _metrics;

    public TestController(
        ILogger<TestController> logger,
        RateLimiterMetrics metrics
        )
    {
        _logger = logger;
        _metrics = metrics;

    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult Get()
    {
        _logger.LogInformation("Request permitido por el rate limiter");
        return Ok(new { message = "Request exitoso", timestamp = DateTimeOffset.UtcNow });
    }

    [HttpGet("metrics")]
    public IActionResult GetMetrics()
    {
        return Ok(new
        {
            totalRequest = _metrics.TotalRequests,
            allowed = _metrics.TotalAllowed,
            blocked = _metrics.TotalBlocked,
        });
    }

    [HttpGet("ordenes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult GetOrdenes()
    {
        _logger.LogInformation("Request permitido en ordenes");
        return Ok(new { endpoint = "ordenes", timestamp = DateTimeOffset.UtcNow });
    }

    [HttpGet("cotizaciones")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult GetCotizaciones()
    {
        _logger.LogInformation("Request permitido en cotizaciones");
        return Ok(new { endpoint = "cotizaciones", timestamp = DateTimeOffset.UtcNow });
    }
}