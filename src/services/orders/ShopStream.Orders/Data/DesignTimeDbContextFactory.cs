using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ShopStream.Orders.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
    public OrdersDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql("Host=localhost;Database=orders_db;Username=shopstream;Password=shopstream")
            .Options);
}
