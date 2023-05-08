using Microsoft.EntityFrameworkCore;
using ShopStream.BuildingBlocks.Messaging;
using ShopStream.Payments.Domain;

namespace ShopStream.Payments.Data;

public sealed class PaymentsDbContext : DbContext, IOutboxDbContext, IInboxDbContext
{
    public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : base(options) { }

    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Payment>(b =>
        {
            b.ToTable("payments");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.HasIndex(p => p.OrderId).IsUnique();
            b.HasIndex(p => p.IdempotencyKey).IsUnique();
            b.Property(p => p.Email).HasMaxLength(256);
            b.Property(p => p.Amount).HasPrecision(12, 2);
            b.Property(p => p.Currency).HasMaxLength(3);
            b.Property(p => p.Provider).HasMaxLength(20);
            b.Property(p => p.ProviderReference).HasMaxLength(100);
            b.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(p => p.FailureReason).HasMaxLength(500);
            b.Property(p => p.IdempotencyKey).HasMaxLength(100);
        });

        modelBuilder.ConfigureOutbox().ConfigureInbox();
    }
}

public sealed class DesignTimeDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseNpgsql("Host=localhost;Database=payments_db;Username=shopstream;Password=shopstream")
            .Options);
}
