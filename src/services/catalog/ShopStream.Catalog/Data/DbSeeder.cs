using Microsoft.EntityFrameworkCore;
using ShopStream.Catalog.Domain;

namespace ShopStream.Catalog.Data;

public static class DbSeeder
{
    public static async Task MigrateAndSeedAsync(IServiceProvider services, ILogger logger, bool seedDemoData)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        if (db.Database.IsRelational())
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            await db.Database.EnsureCreatedAsync();
        }

        if (!seedDemoData || await db.Categories.AnyAsync())
        {
            return;
        }

        db.Categories.AddRange(SeedData.Categories.Select(c => new Category(c.Id, c.Name, c.Slug, $"Hand-picked {c.Name.ToLowerInvariant()}.")));
        db.Products.AddRange(SeedData.Products.Select(p => new Product(
            p.Id, p.CategoryId, p.Sku, p.Name, p.Slug,
            $"{p.Name} - a shop-stream favourite. Built to last and priced fairly.",
            p.Price,
            $"https://picsum.photos/seed/{p.Slug}/640/480",
            p.Attributes)));

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded {Categories} categories and {Products} products", SeedData.Categories.Length, SeedData.Products.Length);
    }
}
