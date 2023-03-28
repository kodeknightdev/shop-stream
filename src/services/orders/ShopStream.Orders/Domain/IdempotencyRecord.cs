namespace ShopStream.Orders.Domain;

public sealed class IdempotencyRecord
{
    private IdempotencyRecord() { }

    public IdempotencyRecord(Guid userId, string key, Guid orderId)
    {
        UserId = userId;
        Key = key;
        OrderId = orderId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid UserId { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public Guid OrderId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
