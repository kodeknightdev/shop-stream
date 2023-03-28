namespace ShopStream.BuildingBlocks.Messaging;

public sealed class OutboxMessage
{
    private OutboxMessage() { }

    public OutboxMessage(string topic, string key, string eventType, string payload, string? correlationId)
    {
        Id = Guid.NewGuid();
        Topic = topic;
        Key = key;
        EventType = eventType;
        Payload = payload;
        CorrelationId = correlationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Topic { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public string? CorrelationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    public void MarkProcessed() => ProcessedAt = DateTimeOffset.UtcNow;

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
    }
}

// inbox row
public sealed class ProcessedEvent
{
    private ProcessedEvent() { }

    public ProcessedEvent(Guid eventId, string eventType)
    {
        EventId = eventId;
        EventType = eventType;
        ProcessedAt = DateTimeOffset.UtcNow;
    }

    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; private set; }
}

public interface IOutboxDbContext
{
    Microsoft.EntityFrameworkCore.DbSet<OutboxMessage> OutboxMessages { get; }
}

public interface IInboxDbContext
{
    Microsoft.EntityFrameworkCore.DbSet<ProcessedEvent> ProcessedEvents { get; }
}
