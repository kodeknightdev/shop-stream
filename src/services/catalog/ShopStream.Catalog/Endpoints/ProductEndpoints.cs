using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShopStream.BuildingBlocks.Auth;
using ShopStream.BuildingBlocks.Errors;
using ShopStream.BuildingBlocks.Kafka;
using ShopStream.BuildingBlocks.Validation;
using ShopStream.Catalog.Caching;
using ShopStream.Catalog.Data;
using ShopStream.Catalog.Domain;
using ShopStream.Contracts;

namespace ShopStream.Catalog.Endpoints;

public static partial class ProductEndpoints
{
    public static RouteGroupBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products").WithTags("Products");

        group.MapGet("/", Search).Produces<PagedResult<ProductSummaryDto>>();
        group.MapGet("/{id:guid}", GetById).Produces<ProductDetailDto>().ProducesProblem(404);
        group.MapGet("/by-slug/{slug}", GetBySlug).Produces<ProductDetailDto>().ProducesProblem(404);

        var admin = group.MapGroup("/").RequireAuthorization(AuthPolicies.AdminOnly);
        admin.MapPost("/", Create).Validate<UpsertProductRequest>().Produces<ProductDetailDto>(201).ProducesProblem(409);
        admin.MapPut("/{id:guid}", Update).Validate<UpsertProductRequest>().Produces<ProductDetailDto>().ProducesProblem(404);
        admin.MapDelete("/{id:guid}", Deactivate).Produces(204);

        return group;
    }

    private static async Task<IResult> Search([AsParameters] ProductQuery query, CatalogDbContext db, HttpContext http, CancellationToken ct)
    {
        if (query.IncludeInactive == true && !http.User.IsAdmin())
        {
            query = query with { IncludeInactive = false };
        }

        if (query.MinPrice is not null && query.MaxPrice is not null && query.MinPrice > query.MaxPrice)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["minPrice"] = new[] { "minPrice must be less than or equal to maxPrice." } });
        }

        Guid? categoryId = null;
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            categoryId = Guid.TryParse(query.Category, out var parsed)
                ? parsed
                : await db.Categories.Where(c => c.Slug == query.Category).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
            if (categoryId is null)
            {
                return Results.Ok(new PagedResult<ProductSummaryDto>(Array.Empty<ProductSummaryDto>(), 1, 0, 0));
            }
        }

        var (page, pageSize) = ProductQueries.Normalize(query.Page, query.PageSize);
        var filtered = db.Products.AsNoTracking().ApplyFilters(query, categoryId);
        var total = await filtered.CountAsync(ct);
        var items = await filtered.ApplySort(query.Sort)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new ProductSummaryDto(p.Id, p.Sku, p.Name, p.Slug, p.Price, p.Currency, p.ImageUrl, p.CategoryId, p.Category!.Name, p.IsActive))
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<ProductSummaryDto>(items, page, pageSize, total));
    }

    private static async Task<IResult> GetById(Guid id, CatalogDbContext db, ProductCache cache, HttpContext http, CancellationToken ct)
    {
        var (product, hit) = await cache.GetOrLoadAsync(ProductCache.IdKey(id), async () =>
        {
            var entity = await db.Products.AsNoTracking().Include(p => p.Category).SingleOrDefaultAsync(p => p.Id == id, ct);
            return entity is null ? null : ToDetail(entity);
        }, ct);
        http.Response.Headers["X-Cache"] = hit ? "HIT" : "MISS";
        return Results.Ok(product ?? throw new NotFoundException($"Product {id} not found."));
    }

    private static async Task<IResult> GetBySlug(string slug, CatalogDbContext db, ProductCache cache, HttpContext http, CancellationToken ct)
    {
        var (product, hit) = await cache.GetOrLoadAsync(ProductCache.SlugKey(slug), async () =>
        {
            var entity = await db.Products.AsNoTracking().Include(p => p.Category).SingleOrDefaultAsync(p => p.Slug == slug, ct);
            return entity is null ? null : ToDetail(entity);
        }, ct);
        http.Response.Headers["X-Cache"] = hit ? "HIT" : "MISS";
        return Results.Ok(product ?? throw new NotFoundException($"Product '{slug}' not found."));
    }

    private static async Task<IResult> Create(UpsertProductRequest request, CatalogDbContext db, CancellationToken ct)
    {
        await EnsureCategoryExists(db, request.CategoryId, ct);
        if (await db.Products.AnyAsync(p => p.Sku == request.Sku, ct))
        {
            throw new ConflictException($"SKU '{request.Sku}' already exists.");
        }

        var slug = await UniqueSlug(db, Slugify(request.Name), null, ct);
        var product = new Product(Guid.NewGuid(), request.CategoryId, request.Sku, request.Name, slug, request.Description,
            request.Price, request.ImageUrl, request.Attributes ?? new Dictionary<string, string>());
        if (!request.IsActive)
        {
            product.Deactivate();
        }

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        await db.Entry(product).Reference(p => p.Category).LoadAsync(ct);
        return Results.Created($"/api/products/{product.Id}", ToDetail(product));
    }

    private static async Task<IResult> Update(Guid id, UpsertProductRequest request, CatalogDbContext db, IEventPublisher publisher,
        ProductCache cache, ILoggerFactory loggers, CancellationToken ct)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException($"Product {id} not found.");
        await EnsureCategoryExists(db, request.CategoryId, ct);
        if (request.Sku != product.Sku)
        {
            throw new BadRequestException("SKU cannot be changed once a product is created.");
        }

        var slug = product.Name == request.Name ? product.Slug : await UniqueSlug(db, Slugify(request.Name), id, ct);
        var oldSlug = product.Slug;
        var oldPrice = product.Update(request.CategoryId, request.Name, slug, request.Description, request.Price, request.ImageUrl,
            request.Attributes ?? new Dictionary<string, string>(), request.IsActive);
        await db.SaveChangesAsync(ct);
        await cache.InvalidateAsync(product.Id, oldSlug, product.Slug);

        if (oldPrice is not null)
        {
            try
            {
                await publisher.PublishAsync(Topics.ProductPriceChanged, product.Id.ToString(), EventTypes.ProductPriceChanged,
                    new ProductPriceChanged(product.Id, product.Sku, oldPrice.Value, product.Price), ct);
            }
            catch (Exception ex)
            {
                // not fatal, checkout re-checks prices anyway
                loggers.CreateLogger("Catalog").LogWarning(ex, "Could not publish price change for {ProductId}", product.Id);
            }
        }

        await db.Entry(product).Reference(p => p.Category).LoadAsync(ct);
        return Results.Ok(ToDetail(product));
    }

    private static async Task<IResult> Deactivate(Guid id, CatalogDbContext db, ProductCache cache, CancellationToken ct)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException($"Product {id} not found.");
        product.Deactivate();
        await db.SaveChangesAsync(ct);
        await cache.InvalidateAsync(product.Id, product.Slug);
        return Results.NoContent();
    }

    private static async Task EnsureCategoryExists(CatalogDbContext db, Guid categoryId, CancellationToken ct)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == categoryId, ct))
        {
            throw new BadRequestException($"Category {categoryId} does not exist.");
        }
    }

    private static async Task<string> UniqueSlug(CatalogDbContext db, string baseSlug, Guid? exceptId, CancellationToken ct)
    {
        var slug = baseSlug;
        var n = 2;
        while (await db.Products.AnyAsync(p => p.Slug == slug && p.Id != exceptId, ct))
        {
            slug = $"{baseSlug}-{n++}";
        }

        return slug;
    }

    internal static string Slugify(string value) => NonSlugChars().Replace(value.ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugChars();

    private static ProductDetailDto ToDetail(Product p) => new(p.Id, p.Sku, p.Name, p.Slug, p.Description, p.Price, p.Currency, p.ImageUrl,
        p.CategoryId, p.Category?.Name ?? string.Empty, p.Category?.Slug ?? string.Empty, p.Attributes, p.IsActive, p.CreatedAt, p.UpdatedAt);
}
