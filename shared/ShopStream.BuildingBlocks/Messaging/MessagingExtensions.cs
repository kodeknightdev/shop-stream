using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopStream.BuildingBlocks.Correlation;
using ShopStream.BuildingBlocks.Kafka;

namespace ShopStream.BuildingBlocks.Messaging;

public static class OutboxExtensions
{
    // goes out with the next SaveChanges, same transaction
    public static void Enqueue<T>(this IOutboxDbContext db, string topic, Guid key, string eventType, T payload)
    {
        var correlationId = CorrelationContext.GetOrCreate();
        db.OutboxMessages.Add(new OutboxMessage(topic, key.ToString(), eventType, KafkaEventPublisher.Serialize(eventType, payload, correlationId), correlationId));
    }

    public static ModelBuilder ConfigureOutbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.Topic).HasMaxLength(200);
            b.Property(m => m.Key).HasMaxLength(100);
            b.Property(m => m.EventType).HasMaxLength(100);
            b.Property(m => m.CorrelationId).HasMaxLength(64);
            b.Property(m => m.LastError).HasMaxLength(1000);
            b.HasIndex(m => new { m.ProcessedAt, m.CreatedAt });
        });
        return modelBuilder;
    }

    public static IServiceCollection AddOutboxRelay<TContext>(this IServiceCollection services, IConfiguration configuration)
        where TContext : DbContext, IOutboxDbContext
    {
        services.Configure<OutboxOptions>(configuration.GetSection("Outbox"));
        services.AddHostedService<OutboxRelay<TContext>>();
        return services;
    }
}

public static class InboxExtensions
{
    public static ModelBuilder ConfigureInbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessedEvent>(b =>
        {
            b.ToTable("processed_events");
            b.HasKey(m => m.EventId);
            b.Property(m => m.EventId).ValueGeneratedNever();
            b.Property(m => m.EventType).HasMaxLength(100);
        });
        return modelBuilder;
    }

    public static Task<bool> AlreadyProcessedAsync(this IInboxDbContext db, Guid eventId, CancellationToken ct) =>
        db.ProcessedEvents.AnyAsync(e => e.EventId == eventId, ct);

    public static void MarkProcessed(this IInboxDbContext db, Guid eventId, string eventType) =>
        db.ProcessedEvents.Add(new ProcessedEvent(eventId, eventType));
}
