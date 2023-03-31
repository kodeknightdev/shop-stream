using Microsoft.Extensions.Options;
using ShopStream.BuildingBlocks.Kafka;
using ShopStream.Contracts;
using ShopStream.Orders.Application;

namespace ShopStream.Orders.Messaging;

public sealed class OrderEventsConsumer : KafkaConsumerService
{
    private static readonly string[] Subscriptions =
    {
        Topics.StockReserved,
        Topics.StockRejected,
        Topics.PaymentSucceeded,
        Topics.PaymentFailed
    };

    public OrderEventsConsumer(IServiceScopeFactory scopeFactory, IOptions<KafkaOptions> options, ILogger<OrderEventsConsumer> logger)
        : base(scopeFactory, options, logger) { }

    protected override IReadOnlyCollection<string> SubscribedTopics => Subscriptions;

    protected override Task HandleAsync(IServiceProvider services, string topic, RawEnvelope envelope, CancellationToken ct)
    {
        var saga = services.GetRequiredService<OrderSagaHandler>();
        return topic switch
        {
            Topics.StockReserved => saga.HandleStockReservedAsync(envelope.EventId, Payload<StockReserved>(envelope), ct),
            Topics.StockRejected => saga.HandleStockRejectedAsync(envelope.EventId, Payload<StockRejected>(envelope), ct),
            Topics.PaymentSucceeded => saga.HandlePaymentSucceededAsync(envelope.EventId, Payload<PaymentSucceeded>(envelope), ct),
            Topics.PaymentFailed => saga.HandlePaymentFailedAsync(envelope.EventId, Payload<PaymentFailed>(envelope), ct),
            _ => Task.CompletedTask
        };
    }
}
