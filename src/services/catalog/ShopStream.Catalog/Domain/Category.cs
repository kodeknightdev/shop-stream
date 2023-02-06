namespace ShopStream.Catalog.Domain;

public sealed class Category
{
    private Category() { }

    public Category(Guid id, string name, string slug, string? description = null)
    {
        Id = id;
        Name = name;
        Slug = slug;
        Description = description;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    public void Rename(string name, string slug, string? description)
    {
        Name = name;
        Slug = slug;
        Description = description;
    }
}
