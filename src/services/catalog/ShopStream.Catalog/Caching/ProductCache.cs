using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using ShopStream.BuildingBlocks.Kafka;
using ShopStream.Catalog.Endpoints;

namespace ShopStream.Catalog.Caching;

public sealed class ProductCacheOptions
{
    public const string SectionName = "ProductCache";

    public int TtlSeconds { get; set; } = 600;
}

// cache-aside for product reads; any redis error just falls back to the db
public sealed class ProductCache
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<ProductCache> _logger;
    private readonly DistributedCacheEntryOptions _entryOptions;

    public ProductCache(IDistributedCache cache, Microsoft.Extensions.Options.IOptions<ProductCacheOptions> options, ILogger<ProductCache> logger)
    {
        _cache = cache;
        _logger = logger;
        _entryOptions = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(options.Value.TtlSeconds) };
    }

    public static string IdKey(Guid id) => $"catalog:product:id:{id}";
    public static string SlugKey(string slug) => $"catalog:product:slug:{slug}";

    public async Task<(ProductDetailDto? Product, bool Hit)> GetOrLoadAsync(string key, Func<Task<ProductDetailDto?>> load, CancellationToken ct)
    {
        try
        {
            var cached = await _cache.GetStringAsync(key, ct);
            if (cached is not null)
            {
                return (JsonSerializer.Deserialize<ProductDetailDto>(cached, JsonDefaults.Options), true);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Product cache read failed for {Key}; using the database", key);
        }

        var product = await load();
        if (product is not null)
        {
            try
            {
                var json = JsonSerializer.Serialize(product, JsonDefaults.Options);
                await _cache.SetStringAsync(IdKey(product.Id), json, _entryOptions, ct);
                await _cache.SetStringAsync(SlugKey(product.Slug), json, _entryOptions, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Product cache write failed for {Key}", key);
            }
        }

        return (product, false);
    }

    public async Task InvalidateAsync(Guid id, params string[] slugs)
    {
        try
        {
            await _cache.RemoveAsync(IdKey(id));
            foreach (var slug in slugs.Distinct())
            {
                await _cache.RemoveAsync(SlugKey(slug));
            }
        }
        catch (Exception ex)
        {
            // worst case it's stale until the TTL
            _logger.LogError(ex, "Could not invalidate cached product {ProductId}", id);
        }
    }
}
