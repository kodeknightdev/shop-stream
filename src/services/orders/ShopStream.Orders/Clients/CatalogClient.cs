using ShopStream.Protos.Catalog;

namespace ShopStream.Orders.Clients;

public sealed record CatalogProduct(Guid Id, string Sku, string Name, decimal Price, string Currency, bool IsActive, string ImageUrl);

public interface ICatalogClient
{
    Task<IReadOnlyDictionary<Guid, CatalogProduct>> GetProductsAsync(IEnumerable<Guid> ids, CancellationToken ct);
}

public sealed class GrpcCatalogClient : ICatalogClient
{
    private readonly CatalogService.CatalogServiceClient _client;
    private readonly CatalogResilience _resilience;

    public GrpcCatalogClient(CatalogService.CatalogServiceClient client, CatalogResilience resilience)
    {
        _client = client;
        _resilience = resilience;
    }

    public async Task<IReadOnlyDictionary<Guid, CatalogProduct>> GetProductsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var request = new GetProductsByIdsRequest();
        request.ProductIds.AddRange(ids.Select(i => i.ToString()));

        var reply = await _resilience.Policy.ExecuteAsync(
            token => _client.GetProductsByIdsAsync(request, deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token).ResponseAsync, ct);

        return reply.Products.ToDictionary(
            p => Guid.Parse(p.Id),
            p => new CatalogProduct(Guid.Parse(p.Id), p.Sku, p.Name, p.PriceCents / 100m, p.Currency, p.IsActive, p.ImageUrl));
    }
}
