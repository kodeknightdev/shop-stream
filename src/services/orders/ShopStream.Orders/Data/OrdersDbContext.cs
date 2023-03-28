using Microsoft.EntityFrameworkCore;
using ShopStream.BuildingBlocks.Messaging;
using ShopStream.Orders.Domain;

namespace ShopStream.Orders.Data;

public sealed class OrdersDbContext : DbContext, IOutboxDbContext, IInboxDbContext
{
    public OrdersDbContext(DbContextOptions<OrdersDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderStatusChange> StatusHistory => Set<OrderStatusChange>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(b =>
        {
            b.ToTable("orders");
            b.HasKey(o => o.Id);
            b.Property(o => o.Number).HasMaxLength(20).IsRequired();
            b.HasIndex(o => o.Number).IsUnique();
            b.Property(o => o.Email).HasMaxLength(256).IsRequired();
            b.Property(o => o.Status).HasConversion<string>().HasMaxLength(32);
            b.Property(o => o.Total).HasPrecision(12, 2);
            b.Property(o => o.Currency).HasMaxLength(3);
            b.Property(o => o.PaymentMethod).HasMaxLength(100);
            b.Property(o => o.CancellationReason).HasMaxLength(500);
            b.HasIndex(o => new { o.UserId, o.CreatedAt });
            b.HasIndex(o => new { o.Status, o.CreatedAt });
            b.OwnsOne(o => o.ShippingAddress, a =>
            {
                a.Property(x => x.FullName).HasMaxLength(200).HasColumnName("ship_full_name");
                a.Property(x => x.Line1).HasMaxLength(200).HasColumnName("ship_line1");
                a.Property(x => x.Line2).HasMaxLength(200).HasColumnName("ship_line2");
                a.Property(x => x.City).HasMaxLength(100).HasColumnName("ship_city");
                a.Property(x => x.PostalCode).HasMaxLength(20).HasColumnName("ship_postal_code");
                a.Property(x => x.Country).HasMaxLength(2).HasColumnName("ship_country");
            });
            b.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(o => o.History).WithOne().HasForeignKey(h => h.OrderId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
            b.Navigation(o => o.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<OrderItem>(b =>
        {
            b.ToTable("order_items");
            b.HasKey(i => i.Id);
            b.Property(i => i.Sku).HasMaxLength(32);
            b.Property(i => i.Name).HasMaxLength(200);
            b.Property(i => i.UnitPrice).HasPrecision(12, 2);
            b.Property(i => i.ImageUrl).HasMaxLength(500);
            b.Ignore(i => i.LineTotal);
        });

        modelBuilder.Entity<OrderStatusChange>(b =>
        {
            b.ToTable("order_status_history");
            b.HasKey(h => h.Id);
            b.Property(h => h.From).HasConversion<string>().HasMaxLength(32);
            b.Property(h => h.To).HasConversion<string>().HasMaxLength(32);
            b.Property(h => h.Note).HasMaxLength(500);
        });

        modelBuilder.ConfigureOutbox().ConfigureInbox();

        modelBuilder.Entity<IdempotencyRecord>(b =>
        {
            b.ToTable("idempotency_keys");
            b.HasKey(r => new { r.UserId, r.Key });
            b.Property(r => r.Key).HasMaxLength(100);
        });
    }
}
