using Microsoft.EntityFrameworkCore;
using ShopStream.Contracts;
using ShopStream.Orders.Domain;

namespace ShopStream.Orders.Data;

// Development only: a few past orders for Jane. Product ids/prices match the catalog seed.
public static class DemoOrderSeeder
{
    private sealed record Line(Guid ProductId, string Sku, string Name, int Quantity, decimal Price, string Slug);

    private static readonly Line Headphones = new(Guid.Parse("84db4db8-b982-50fd-9f18-1156b1cfeb6d"), "E-1001", "Aurora Wireless Headphones", 1, 129.99m, "aurora-wireless-headphones");
    private static readonly Line NightGarden = new(Guid.Parse("35a25877-1ae7-5dcd-8c92-f25de0de1b5a"), "B-1017", "Night Garden", 2, 16.99m, "night-garden");
    private static readonly Line YogaMat = new(Guid.Parse("96d1aaed-7922-50fd-b842-cb0d73d55b54"), "SO-1025", "Flow Yoga Mat", 1, 45.00m, "flow-yoga-mat");
    private static readonly Line Bottle = new(Guid.Parse("b09a7e72-39b3-559c-8e31-f7549f90dbf5"), "SO-1024", "Ridge Insulated Bottle", 2, 27.50m, "ridge-insulated-bottle");
    private static readonly Line Tent = new(Guid.Parse("c3e098d9-04d2-521e-9339-d4daad154914"), "SO-1027", "Basecamp 2-Person Tent", 1, 189.00m, "basecamp-2-person-tent");

    // new instance each time - owned types can't be shared between orders
    private static ShippingAddress JaneAddress() => new("Jane Doe", "42 Harbour Road", null, "Portland", "97201", "US");

    public static async Task SeedAsync(OrdersDbContext db, ILogger logger)
    {
        if (await db.Orders.AnyAsync(o => o.UserId == DemoAccounts.JaneId))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        Add(db, DemoAccounts.JaneOrder1, "SS-DEMO-000001", now.AddDays(-21), "pm_card_visa", new[] { Headphones, NightGarden }, cancelReason: null);
        Add(db, DemoAccounts.JaneOrder2, "SS-DEMO-000002", now.AddDays(-9), "pm_card_mastercard", new[] { YogaMat, Bottle }, cancelReason: null);
        Add(db, DemoAccounts.JaneOrder3, "SS-DEMO-000003", now.AddDays(-3), "pm_card_chargeDeclined", new[] { Tent },
            cancelReason: "Payment failed: Your card was declined.");

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded demo order history for {Email}", DemoAccounts.JaneEmail);
    }

    private static void Add(OrdersDbContext db, Guid id, string number, DateTimeOffset placedAt, string paymentMethod, Line[] lines, string? cancelReason)
    {
        var order = Order.Place(DemoAccounts.JaneId, DemoAccounts.JaneEmail, JaneAddress(), paymentMethod,
            lines.Select(l => new OrderItem(l.ProductId, l.Sku, l.Name, l.Quantity, l.Price, $"https://picsum.photos/seed/{l.Slug}/640/480")), id: id);

        // use the real state machine so the history looks like the saga's
        order.MarkInventoryReserved(Guid.NewGuid());
        if (cancelReason is null)
        {
            order.MarkPaid(Guid.NewGuid());
            order.Confirm();
        }
        else
        {
            order.Cancel(cancelReason);
        }

        db.Orders.Add(order);

        // backdate via the change tracker
        var entry = db.Entry(order);
        entry.Property(o => o.Number).CurrentValue = number;
        entry.Property(o => o.CreatedAt).CurrentValue = placedAt;
        entry.Property(o => o.UpdatedAt).CurrentValue = placedAt.AddSeconds(order.History.Count * 2);
        var step = 0;
        foreach (var change in order.History)
        {
            db.Entry(change).Property(h => h.OccurredAt).CurrentValue = placedAt.AddSeconds(step++ * 2);
        }
    }
}
