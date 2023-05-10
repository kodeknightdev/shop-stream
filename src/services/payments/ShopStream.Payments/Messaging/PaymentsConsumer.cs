using Microsoft.Extensions.Options;
using ShopStream.BuildingBlocks.Kafka;
using ShopStream.Contracts;
using ShopStream.Payments.Application;

namespace ShopStream.Payments.Messaging;

public sealed class PaymentsConsumer : KafkaConsumerService
{
    private static readonly string[] Subscriptions = { Topics.StockReserved };

    public PaymentsConsumer(IServiceScopeFactory scopeFactory, IOptions<KafkaOptions> options, ILogger<PaymentsConsumer> logger)
        : base(scopeFactory, options, logger) { }

    protected override IReadOnlyCollection<string> SubscribedTopics => Subscriptions;

    protected override async Task HandleAsync(IServiceProvider services, string topic, RawEnvelope envelope, CancellationToken ct)
    {
        if (topic == Topics.StockReserved)
        {
            await services.GetRequiredService<PaymentProcessor>().HandleStockReservedAsync(envelope.EventId, Payload<StockReserved>(envelope), ct);
        }
    }
}
