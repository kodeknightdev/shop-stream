namespace ShopStream.Contracts;

// same JSON shape in every service; bump SchemaVersion for breaking payload changes
public sealed record EventEnvelope<T>(
    Guid EventId,
    string EventType,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    string? CorrelationId,
    T Payload)
{
    public static EventEnvelope<T> Create(string eventType, T payload, string? correlationId, int schemaVersion = 1) =>
        new(Guid.NewGuid(), eventType, schemaVersion, DateTimeOffset.UtcNow, correlationId, payload);
}

public sealed record RawEnvelope(
    Guid EventId,
    string EventType,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    string? CorrelationId,
    System.Text.Json.JsonElement Payload);
