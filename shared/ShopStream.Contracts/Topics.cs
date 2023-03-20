namespace ShopStream.Contracts;

// saga topics are keyed by orderId
public static class Topics
{
    public const string OrderPlaced = "orders.order-placed";
    public const string OrderConfirmed = "orders.order-confirmed";
    public const string OrderCancelled = "orders.order-cancelled";
    public const string StockReserved = "inventory.stock-reserved";
    public const string StockRejected = "inventory.stock-rejected";
    public const string PaymentSucceeded = "payments.payment-succeeded";
    public const string PaymentFailed = "payments.payment-failed";
    public const string ProductPriceChanged = "catalog.product-price-changed";

    public static readonly string[] All =
    {
        OrderPlaced, OrderConfirmed, OrderCancelled, StockReserved, StockRejected, PaymentSucceeded, PaymentFailed, ProductPriceChanged
    };

    public const string RetrySuffix = ".retry";
    public const string DeadLetterSuffix = ".dlq";

    public static string Retry(string topic) => topic + RetrySuffix;
    public static string DeadLetter(string topic) => topic + DeadLetterSuffix;
}

public static class KafkaHeaders
{
    public const string CorrelationId = "correlation-id";
    public const string EventType = "event-type";
    public const string SchemaVersion = "schema-version";
    public const string RetryCount = "x-retry-count";
    public const string OriginalTopic = "x-original-topic";
    public const string Error = "x-error";

    // retry topics are shared between groups, this says whose copy it is
    public const string ConsumerGroup = "x-consumer-group";

    // unix ms, don't process before this
    public const string NotBefore = "x-not-before";
}
