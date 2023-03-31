using Microsoft.EntityFrameworkCore;
using ShopStream.Contracts;
using ShopStream.Orders.Data;
using ShopStream.Orders.Domain;
using ShopStream.BuildingBlocks.Messaging;

namespace ShopStream.Orders.Application;

// event ids go into processed_events with the same SaveChanges, so redeliveries are skipped
public sealed class OrderSagaHandler
{
    private readonly OrdersDbContext _db;
    private readonly ILogger<OrderSagaHandler> _logger;

    public OrderSagaHandler(OrdersDbContext db, ILogger<OrderSagaHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task HandleStockReservedAsync(Guid eventId, StockReserved e, CancellationToken ct) =>
        WithOrderAsync(eventId, EventTypes.StockReserved, e.OrderId, order =>
        {
            order.MarkInventoryReserved(e.ReservationId);
        }, ct);

    public Task HandleStockRejectedAsync(Guid eventId, StockRejected e, CancellationToken ct) =>
        WithOrderAsync(eventId, EventTypes.StockRejected, e.OrderId, order =>
        {
            var reason = $"Inventory: {e.Reason}";
            if (order.Cancel(reason))
            {
                _db.Enqueue(Topics.OrderCancelled, order.Id, EventTypes.OrderCancelled, new OrderCancelled(order.Id, order.UserId, order.Email, reason));
            }
        }, ct);

    public Task HandlePaymentSucceededAsync(Guid eventId, PaymentSucceeded e, CancellationToken ct) =>
        WithOrderAsync(eventId, EventTypes.PaymentSucceeded, e.OrderId, order =>
        {
            if (order.Status == OrderStatus.Pending)
            {
                // stock-reserved not handled yet, let the retry topic bring it back
                throw new InvalidOrderTransitionException(order.Status, OrderStatus.Paid);
            }

            order.MarkPaid(e.PaymentId);
            if (order.Confirm())
            {
                _db.Enqueue(Topics.OrderConfirmed, order.Id, EventTypes.OrderConfirmed,
                    new OrderConfirmed(order.Id, order.UserId, order.Email, order.Total, order.Currency));
            }
        }, ct);

    public Task HandlePaymentFailedAsync(Guid eventId, PaymentFailed e, CancellationToken ct) =>
        WithOrderAsync(eventId, EventTypes.PaymentFailed, e.OrderId, order =>
        {
            order.Cancel($"Payment failed: {e.Reason}");
        }, ct);

    private async Task WithOrderAsync(Guid eventId, string eventType, Guid orderId, Action<Order> apply, CancellationToken ct)
    {
        if (await _db.AlreadyProcessedAsync(eventId, ct))
        {
            _logger.LogInformation("Skipping duplicate {EventType} {EventId}", eventType, eventId);
            return;
        }

        var order = await _db.Orders.Include(o => o.History).SingleOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null)
        {
            _logger.LogWarning("{EventType} for unknown order {OrderId}", eventType, orderId);
        }
        else
        {
            var before = order.Status;
            apply(order);
            if (before != order.Status)
            {
                _logger.LogInformation("Order {OrderId}: {From} -> {To}", order.Id, before, order.Status);
            }
        }

        _db.MarkProcessed(eventId, eventType);
        await _db.SaveChangesAsync(ct);
    }
}
