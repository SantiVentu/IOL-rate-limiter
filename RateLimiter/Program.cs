using RateLimiter.Configuration;
using RateLimiter.Core;
using RateLimiter.Metrics;
using RateLimiter.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RateLimiterOptions>(
    builder.Configuration.GetSection(RateLimiterOptions.SectionName)
);

// Singleton porque todo el estado vive en el ConcurrentDictionary dentro de la clase
builder.Services.AddSingleton<IRateLimiter, SlidingWindowRL>();
builder.Services.AddSingleton<RateLimiterMetrics>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseMiddleware<RateLimiterMiddleware>();

app.MapControllers();

app.Run();

// Necesario para que el proyecto de tests pueda acceder al entry point
public partial class Program { }