namespace ShopStream.Payments.Domain;

public enum PaymentStatus
{
    Succeeded,
    Failed
}

// one per order (unique index on OrderId)
public sealed class Payment
{
    private Payment() { }

    public Payment(Guid orderId, Guid userId, string email, decimal amount, string currency, string provider, string idempotencyKey)
    {
        Id = Guid.NewGuid();
        OrderId = orderId;
        UserId = userId;
        Email = email;
        Amount = amount;
        Currency = currency;
        Provider = provider;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid UserId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "USD";
    public string Provider { get; private set; } = string.Empty;
    public string? ProviderReference { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public void Succeed(string? providerReference)
    {
        Status = PaymentStatus.Succeeded;
        ProviderReference = providerReference;
        FailureReason = null;
    }

    public void Fail(string reason, string? providerReference)
    {
        Status = PaymentStatus.Failed;
        FailureReason = reason;
        ProviderReference = providerReference;
    }
}
