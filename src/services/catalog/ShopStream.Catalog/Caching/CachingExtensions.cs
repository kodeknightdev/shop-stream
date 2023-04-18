using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using ShopStream.BuildingBlocks.Health;

namespace ShopStream.Catalog.Caching;

public static class CachingExtensions
{
    // in-memory cache when there's no redis connection string (tests, quick local runs)
    public static IServiceCollection AddProductCache(this IServiceCollection services, IConfiguration configuration)
    {
        // resolved lazily so test config overrides apply
        services.AddSingleton<IDistributedCache>(sp =>
        {
            var redis = sp.GetRequiredService<IConfiguration>().GetConnectionString("Redis");
            return string.IsNullOrWhiteSpace(redis)
                ? new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()))
                : new RedisCache(Options.Create(new RedisCacheOptions { Configuration = redis, InstanceName = "shopstream:" }));
        });
        services.AddHealthChecks().AddCheck<RedisCacheHealthCheck>("redis", tags: new[] { HealthEndpoints.ReadyTag });

        services.Configure<ProductCacheOptions>(configuration.GetSection(ProductCacheOptions.SectionName));
        services.AddSingleton<ProductCache>();
        return services;
    }
}

public sealed class RedisCacheHealthCheck : IHealthCheck
{
    private readonly IDistributedCache _cache;

    public RedisCacheHealthCheck(IDistributedCache cache) => _cache = cache;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.GetStringAsync("catalog:health", cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis unreachable", ex);
        }
    }
}
