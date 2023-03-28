using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShopStream.BuildingBlocks.Kafka;

namespace ShopStream.BuildingBlocks.Messaging;

public sealed class OutboxOptions
{
    public int PollIntervalMs { get; set; } = 1000;
    public int BatchSize { get; set; } = 50;
    public int MaxAttempts { get; set; } = 10;
}

// SKIP LOCKED so several replicas can run this. Still at-least-once - consumers dedupe.
public sealed class OutboxRelay<TContext> : BackgroundService where TContext : DbContext, IOutboxDbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventPublisher _publisher;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxRelay<TContext>> _logger;

    public OutboxRelay(IServiceScopeFactory scopeFactory, IEventPublisher publisher, IOptions<OutboxOptions> options, ILogger<OutboxRelay<TContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.PollIntervalMs));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PublishPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Outbox relay iteration failed");
            }
        }
    }

    public async Task<int> PublishPendingAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;

        var batch = db.Database.IsRelational()
            ? await db.OutboxMessages.FromSqlRaw(
                    "SELECT * FROM outbox_messages WHERE \"ProcessedAt\" IS NULL AND \"Attempts\" < {0} ORDER BY \"CreatedAt\" LIMIT {1} FOR UPDATE SKIP LOCKED",
                    _options.MaxAttempts, _options.BatchSize)
                .ToListAsync(ct)
            : await db.OutboxMessages.Where(m => m.ProcessedAt == null && m.Attempts < _options.MaxAttempts)
                .OrderBy(m => m.CreatedAt).Take(_options.BatchSize).ToListAsync(ct);

        var published = 0;
        foreach (var message in batch)
        {
            try
            {
                await _publisher.PublishRawAsync(message.Topic, message.Key, message.EventType, message.Payload, message.CorrelationId, ct);
                message.MarkProcessed();
                published++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to relay outbox message {MessageId} ({EventType})", message.Id, message.EventType);
                message.MarkFailed(ex.Message);
                // stop here to keep per-order ordering, retry next tick
                break;
            }
        }

        await db.SaveChangesAsync(ct);
        if (tx is not null)
        {
            await tx.CommitAsync(ct);
        }

        return published;
    }
}
