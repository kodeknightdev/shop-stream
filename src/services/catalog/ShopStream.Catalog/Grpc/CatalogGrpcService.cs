using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using ShopStream.Catalog.Data;
using ShopStream.Catalog.Domain;
using ShopStream.Protos.Catalog;

namespace ShopStream.Catalog.Grpc;

public sealed class CatalogGrpcService : CatalogService.CatalogServiceBase
{
    private readonly CatalogDbContext _db;

    public CatalogGrpcService(CatalogDbContext db) => _db = db;

    public override async Task<ProductReply> GetProduct(GetProductRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ProductId, out var id))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "product_id must be a GUID"));
        }

        var product = await _db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, context.CancellationToken)
                      ?? throw new RpcException(new Status(StatusCode.NotFound, $"Product {id} not found"));
        return ToReply(product);
    }

    public override async Task<GetProductsByIdsReply> GetProductsByIds(GetProductsByIdsRequest request, ServerCallContext context)
    {
        var ids = request.ProductIds.Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Distinct().ToList();
        if (ids.Count > 200)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "At most 200 ids per call"));
        }

        var products = await _db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(context.CancellationToken);
        var reply = new GetProductsByIdsReply();
        reply.Products.AddRange(products.Select(ToReply));
        reply.MissingIds.AddRange(request.ProductIds.Except(products.Select(p => p.Id.ToString()), StringComparer.OrdinalIgnoreCase));
        return reply;
    }

    private static ProductReply ToReply(Product p) => new()
    {
        Id = p.Id.ToString(),
        Sku = p.Sku,
        Name = p.Name,
        PriceCents = p.PriceCents,
        Currency = p.Currency,
        IsActive = p.IsActive,
        ImageUrl = p.ImageUrl
    };
}
