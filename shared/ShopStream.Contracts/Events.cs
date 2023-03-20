namespace ShopStream.Contracts;

public static class EventTypes
{
    public const string OrderPlaced = "OrderPlaced";
    public const string OrderConfirmed = "OrderConfirmed";
    public const string OrderCancelled = "OrderCancelled";
    public const string StockReserved = "StockReserved";
    public const string StockRejected = "StockRejected";
    public const string PaymentSucceeded = "PaymentSucceeded";
    public const string PaymentFailed = "PaymentFailed";
    public const string ProductPriceChanged = "ProductPriceChanged";
}

public sealed record OrderLineDto(Guid ProductId, string Sku, string Name, int Quantity, decimal UnitPrice);

public sealed record OrderPlaced(
    Guid OrderId,
    Guid UserId,
    string Email,
    IReadOnlyList<OrderLineDto> Items,
    decimal Total,
    string Currency,
    string PaymentMethod);

public sealed record StockReserved(
    Guid OrderId,
    Guid ReservationId,
    Guid UserId,
    string Email,
    decimal Total,
    string Currency,
    string PaymentMethod,
    DateTimeOffset ExpiresAt);

public sealed record StockRejected(Guid OrderId, string Reason);

public sealed record PaymentSucceeded(Guid OrderId, Guid PaymentId, decimal Amount, string Currency, string Provider, string? ProviderReference);

public sealed record PaymentFailed(Guid OrderId, Guid PaymentId, Guid UserId, string Email, decimal Amount, string Reason);

public sealed record OrderConfirmed(Guid OrderId, Guid UserId, string Email, decimal Total, string Currency);

public sealed record OrderCancelled(Guid OrderId, Guid UserId, string Email, string Reason);

public sealed record ProductPriceChanged(Guid ProductId, string Sku, decimal OldPrice, decimal NewPrice);
