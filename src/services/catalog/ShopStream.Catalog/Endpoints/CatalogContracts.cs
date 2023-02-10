using FluentValidation;

namespace ShopStream.Catalog.Endpoints;

public sealed record CategoryDto(Guid Id, string Name, string Slug, string? Description, int ProductCount);

public sealed record ProductSummaryDto(Guid Id, string Sku, string Name, string Slug, decimal Price, string Currency, string ImageUrl,
    Guid CategoryId, string CategoryName, bool IsActive);

public sealed record ProductDetailDto(Guid Id, string Sku, string Name, string Slug, string Description, decimal Price, string Currency,
    string ImageUrl, Guid CategoryId, string CategoryName, string CategorySlug, IReadOnlyDictionary<string, string> Attributes, bool IsActive,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

public sealed record UpsertProductRequest(
    Guid CategoryId,
    string Sku,
    string Name,
    string Description,
    decimal Price,
    string ImageUrl,
    Dictionary<string, string>? Attributes,
    bool IsActive = true);

public sealed record UpsertCategoryRequest(string Name, string Slug, string? Description);

public sealed record ProductQuery(string? Search, string? Category, decimal? MinPrice, decimal? MaxPrice, string? Sort, int? Page, int? PageSize, bool? IncludeInactive);

public sealed class UpsertProductRequestValidator : AbstractValidator<UpsertProductRequest>
{
    public UpsertProductRequestValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(32).Matches("^[A-Z0-9-]+$").WithMessage("SKU may only contain A-Z, 0-9 and dashes.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Price).GreaterThan(0).LessThan(100_000).PrecisionScale(12, 2, true);
        RuleFor(x => x.ImageUrl).NotEmpty().MaximumLength(500)
            .Must(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
            .WithMessage("ImageUrl must be an absolute http(s) URL.");
        RuleForEach(x => x.Attributes).Must(kv => kv.Key.Length <= 50 && kv.Value.Length <= 200)
            .WithMessage("Attribute keys are limited to 50 characters and values to 200.");
    }
}

public sealed class UpsertCategoryRequestValidator : AbstractValidator<UpsertCategoryRequest>
{
    public UpsertCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(100).Matches("^[a-z0-9-]+$");
        RuleFor(x => x.Description).MaximumLength(500);
    }
}
