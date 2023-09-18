using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ShopStream.BuildingBlocks.Kafka;
using ShopStream.Contracts;
using ShopStream.Orders.Application;
using ShopStream.Orders.Data;
using ShopStream.Orders.Domain;

namespace ShopStream.Orders.UnitTests;

public class OrderSagaHandlerTests
{
    private readonly OrdersDbContext _db = new(new DbContextOptionsBuilder<OrdersDbContext>()
        .UseInMemoryDatabase($"saga-{Guid.NewGuid():N}").Options);

    private OrderSagaHandler Handler => new(_db, NullLogger<OrderSagaHandler>.Instance);

    private async Task<Order> SeedOrderAsync()
    {
        var order = Order.Place(Guid.NewGuid(), "casey@example.com", new ShippingAddress("Casey", "1 Main", null, "Town", "123", "US"),
            "pm_card_visa", new[] { new OrderItem(Guid.NewGuid(), "SKU-1", "Thing", 1, 20m) });
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return order;
    }

    private Task<Order> ReloadAsync(Guid id) => _db.Orders.AsNoTracking().Include(o => o.History).SingleAsync(o => o.Id == id);

    private List<(string Topic, JsonElement Payload)> Outbox() => _db.OutboxMessages.AsNoTracking().ToList()
        .Select(m => (m.Topic, JsonDocument.Parse(m.Payload).RootElement.GetProperty("payload")))
        .ToList();

    [Fact]
    public async Task Happy_path_confirms_and_publishes_order_confirmed()
    {
        var order = await SeedOrderAsync();

        await Handler.HandleStockReservedAsync(Guid.NewGuid(), new StockReserved(order.Id, Guid.NewGuid(), order.UserId, order.Email, 20m, "USD", "pm_card_visa", DateTimeOffset.UtcNow.AddMinutes(15)), default);
        await Handler.HandlePaymentSucceededAsync(Guid.NewGuid(), new PaymentSucceeded(order.Id, Guid.NewGuid(), 20m, "USD", "mock", "mock_pi_1"), default);

        var reloaded = await ReloadAsync(order.Id);
        reloaded.Status.Should().Be(OrderStatus.Confirmed);
        reloaded.History.Select(h => h.To).Should().Contain(new[] { OrderStatus.InventoryReserved, OrderStatus.Paid, OrderStatus.Confirmed });

        var outbox = Outbox();
        outbox.Should().ContainSingle(m => m.Topic == Topics.OrderConfirmed);
        outbox.Single().Payload.GetProperty("orderId").GetGuid().Should().Be(order.Id);
    }

    [Fact]
    public async Task Stock_rejection_cancels_with_reason()
    {
        var order = await SeedOrderAsync();

        await Handler.HandleStockRejectedAsync(Guid.NewGuid(), new StockRejected(order.Id, "Insufficient stock: SKU-1"), default);

        var reloaded = await ReloadAsync(order.Id);
        reloaded.Status.Should().Be(OrderStatus.Cancelled);
        reloaded.CancellationReason.Should().Be("Inventory: Insufficient stock: SKU-1");
        Outbox().Should().ContainSingle(m => m.Topic == Topics.OrderCancelled);
    }

    [Fact]
    public async Task Payment_failure_compensates_by_publishing_order_cancelled()
    {
        var order = await SeedOrderAsync();
        await Handler.HandleStockReservedAsync(Guid.NewGuid(), new StockReserved(order.Id, Guid.NewGuid(), order.UserId, order.Email, 20m, "USD", "pm_card_chargeDeclined", DateTimeOffset.UtcNow.AddMinutes(15)), default);

        await Handler.HandlePaymentFailedAsync(Guid.NewGuid(), new PaymentFailed(order.Id, Guid.NewGuid(), order.UserId, order.Email, 20m, "Your card was declined."), default);

        var reloaded = await ReloadAsync(order.Id);
        reloaded.Status.Should().Be(OrderStatus.Cancelled);
        reloaded.CancellationReason.Should().Contain("card was declined");
        var cancelled = Outbox().Single(m => m.Topic == Topics.OrderCancelled);
        cancelled.Payload.GetProperty("reason").GetString().Should().StartWith("Payment failed");
    }

    [Fact]
    public async Task Duplicate_events_are_ignored()
    {
        var order = await SeedOrderAsync();
        var eventId = Guid.NewGuid();
        var rejected = new StockRejected(order.Id, "none left");

        await Handler.HandleStockRejectedAsync(eventId, rejected, default);
        await Handler.HandleStockRejectedAsync(eventId, rejected, default);

        Outbox().Should().HaveCount(1);
        (await _db.ProcessedEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Payment_before_reservation_throws_so_the_message_is_retried()
    {
        var order = await SeedOrderAsync();
        var act = () => Handler.HandlePaymentSucceededAsync(Guid.NewGuid(), new PaymentSucceeded(order.Id, Guid.NewGuid(), 20m, "USD", "mock", null), default);
        await act.Should().ThrowAsync<InvalidOrderTransitionException>();
        (await _db.ProcessedEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Late_reservation_for_cancelled_order_is_ignored()
    {
        var order = await SeedOrderAsync();
        await Handler.HandleStockRejectedAsync(Guid.NewGuid(), new StockRejected(order.Id, "gone"), default);

        await Handler.HandleStockReservedAsync(Guid.NewGuid(), new StockReserved(order.Id, Guid.NewGuid(), order.UserId, order.Email, 20m, "USD", "pm_card_visa", DateTimeOffset.UtcNow), default);

        (await ReloadAsync(order.Id)).Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void Outbox_payload_uses_the_shared_envelope()
    {
        var json = KafkaEventPublisher.Serialize(EventTypes.OrderCancelled, new OrderCancelled(Guid.NewGuid(), Guid.NewGuid(), "a@b.c", "x"), "corr-1");
        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("eventType").GetString().Should().Be("OrderCancelled");
        root.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        root.GetProperty("correlationId").GetString().Should().Be("corr-1");
        root.GetProperty("payload").GetProperty("reason").GetString().Should().Be("x");
    }
}
