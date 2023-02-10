using ShopStream.Catalog.Domain;

namespace ShopStream.Catalog.Endpoints;

public static class ProductQueries
{
    public const int MaxPageSize = 60;

    public static IQueryable<Product> ApplyFilters(this IQueryable<Product> query, ProductQuery q, Guid? categoryId)
    {
        if (q.IncludeInactive != true)
        {
            query = query.Where(p => p.IsActive);
        }

        if (categoryId is not null)
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(term) || p.Description.ToLower().Contains(term) || p.Sku.ToLower().Contains(term));
        }

        if (q.MinPrice is not null)
        {
            query = query.Where(p => p.Price >= q.MinPrice);
        }

        if (q.MaxPrice is not null)
        {
            query = query.Where(p => p.Price <= q.MaxPrice);
        }

        return query;
    }

    public static IQueryable<Product> ApplySort(this IQueryable<Product> query, string? sort) => sort?.ToLowerInvariant() switch
    {
        "price_asc" => query.OrderBy(p => p.Price).ThenBy(p => p.Name),
        "price_desc" => query.OrderByDescending(p => p.Price).ThenBy(p => p.Name),
        "newest" => query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Name),
        "name_desc" => query.OrderByDescending(p => p.Name),
        _ => query.OrderBy(p => p.Name)
    };

    public static (int Page, int PageSize) Normalize(int? page, int? pageSize) =>
        (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 12, 1, MaxPageSize));
}
