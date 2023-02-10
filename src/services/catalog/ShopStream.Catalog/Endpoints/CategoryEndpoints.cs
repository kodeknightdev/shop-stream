using Microsoft.EntityFrameworkCore;
using ShopStream.BuildingBlocks.Auth;
using ShopStream.BuildingBlocks.Errors;
using ShopStream.BuildingBlocks.Validation;
using ShopStream.Catalog.Data;
using ShopStream.Catalog.Domain;

namespace ShopStream.Catalog.Endpoints;

public static class CategoryEndpoints
{
    public static RouteGroupBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/categories").WithTags("Categories");

        group.MapGet("/", async (CatalogDbContext db, CancellationToken ct) =>
        {
            var counts = await db.Products.Where(p => p.IsActive).GroupBy(p => p.CategoryId)
                .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var categories = await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
            return Results.Ok(categories.Select(c => new CategoryDto(c.Id, c.Name, c.Slug, c.Description, counts.GetValueOrDefault(c.Id))));
        }).Produces<IEnumerable<CategoryDto>>();

        group.MapPost("/", async (UpsertCategoryRequest request, CatalogDbContext db, CancellationToken ct) =>
        {
            if (await db.Categories.AnyAsync(c => c.Slug == request.Slug, ct))
            {
                throw new ConflictException($"Category slug '{request.Slug}' is already used.");
            }

            var category = new Category(Guid.NewGuid(), request.Name, request.Slug, request.Description);
            db.Categories.Add(category);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/categories/{category.Id}", new CategoryDto(category.Id, category.Name, category.Slug, category.Description, 0));
        }).RequireAuthorization(AuthPolicies.AdminOnly).Validate<UpsertCategoryRequest>();

        group.MapPut("/{id:guid}", async (Guid id, UpsertCategoryRequest request, CatalogDbContext db, CancellationToken ct) =>
        {
            var category = await db.Categories.FindAsync(new object[] { id }, ct) ?? throw new NotFoundException($"Category {id} not found.");
            if (await db.Categories.AnyAsync(c => c.Slug == request.Slug && c.Id != id, ct))
            {
                throw new ConflictException($"Category slug '{request.Slug}' is already used.");
            }

            category.Rename(request.Name, request.Slug, request.Description);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new CategoryDto(category.Id, category.Name, category.Slug, category.Description, 0));
        }).RequireAuthorization(AuthPolicies.AdminOnly).Validate<UpsertCategoryRequest>();

        return group;
    }
}
