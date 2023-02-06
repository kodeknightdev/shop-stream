namespace ShopStream.Catalog.Domain;

public sealed class Product
{
    private Product() { }

    public Product(Guid id, Guid categoryId, string sku, string name, string slug, string description, decimal price,
        string imageUrl, Dictionary<string, string> attributes)
    {
        if (price <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Price must be positive.");
        }

        Id = id;
        CategoryId = categoryId;
        Sku = sku;
        Name = name;
        Slug = slug;
        Description = description;
        Price = price;
        ImageUrl = imageUrl;
        Attributes = attributes;
        IsActive = true;
        CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CategoryId { get; private set; }
    public Category? Category { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = "USD";
    public string ImageUrl { get; private set; } = string.Empty;
    public Dictionary<string, string> Attributes { get; private set; } = new();
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // returns the old price if it changed
    public decimal? Update(Guid categoryId, string name, string slug, string description, decimal price, string imageUrl,
        Dictionary<string, string> attributes, bool isActive)
    {
        if (price <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Price must be positive.");
        }

        var oldPrice = Price;
        CategoryId = categoryId;
        Name = name;
        Slug = slug;
        Description = description;
        Price = price;
        ImageUrl = imageUrl;
        Attributes = attributes;
        IsActive = isActive;
        UpdatedAt = DateTimeOffset.UtcNow;
        return oldPrice != price ? oldPrice : null;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public long PriceCents => (long)Math.Round(Price * 100m, MidpointRounding.AwayFromZero);
}
