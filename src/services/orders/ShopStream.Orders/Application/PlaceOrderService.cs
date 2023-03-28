using Microsoft.EntityFrameworkCore;
using ShopStream.BuildingBlocks.Errors;
using ShopStream.Contracts;
using ShopStream.Orders.Clients;
using ShopStream.Orders.Data;
using ShopStream.Orders.Domain;
using ShopStream.BuildingBlocks.Messaging;

namespace ShopStream.Orders.Application;

public sealed record PlaceOrderResult(Order Order, bool Replayed, IReadOnlyList<PriceAdjustmentDto> PriceAdjustments);

public sealed class PlaceOrderService
{
    private const int MaxQuantityPerLine = 20;

    private readonly OrdersDbContext _db;
    private readonly ICartClient _cart;
    private readonly ICatalogClient _catalog;
    private readonly ILogger<PlaceOrderService> _logger;

    public PlaceOrderService(OrdersDbContext db, ICartClient cart, ICatalogClient catalog, ILogger<PlaceOrderService> logger)
    {
        _db = db;
        _cart = cart;
        _catalog = catalog;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceAsync(Guid userId, string email, string bearerToken, PlaceOrderRequest request, string? idempotencyKey, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await _db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(r => r.UserId == userId && r.Key == idempotencyKey, ct);
            if (existing is not null)
            {
                var replay = await LoadAsync(existing.OrderId, ct);
                return new PlaceOrderResult(replay, true, Array.Empty<PriceAdjustmentDto>());
            }
        }

        var cart = await _cart.GetCartAsync(bearerToken, ct);
        if (cart is null || cart.Items.Count == 0)
        {
            throw new BadRequestException("Your cart is empty.");
        }

        if (cart.Items.Any(i => i.Quantity is <= 0 or > MaxQuantityPerLine))
        {
            throw new BadRequestException($"Quantities must be between 1 and {MaxQuantityPerLine}.");
        }

        IReadOnlyDictionary<Guid, CatalogProduct> products;
        try
        {
            products = await _catalog.GetProductsAsync(cart.Items.Select(i => i.ProductId).Distinct(), ct);
        }
        catch (Exception ex) when (ResiliencePolicies.IsDependencyFailure(ex))
        {
            _logger.LogError(ex, "Catalog gRPC call failed: {Reason}", ex.Message);
            throw new ConflictException("Prices could not be confirmed right now. Please try again.");
        }

        var unavailable = cart.Items.Where(i => !products.TryGetValue(i.ProductId, out var p) || !p.IsActive).Select(i => i.Name).ToList();
        if (unavailable.Count > 0)
        {
            throw new ConflictException($"No longer available: {string.Join(", ", unavailable)}. Remove them from your cart and try again.");
        }

        // always the catalog price, the cart is just what the customer last saw
        var adjustments = new List<PriceAdjustmentDto>();
        var items = new List<OrderItem>();
        foreach (var line in cart.Items.GroupBy(i => i.ProductId))
        {
            var product = products[line.Key];
            var quantity = line.Sum(i => i.Quantity);
            var cartPrice = line.First().UnitPrice;
            if (cartPrice != product.Price)
            {
                adjustments.Add(new PriceAdjustmentDto(product.Id, product.Name, cartPrice, product.Price));
            }

            items.Add(new OrderItem(product.Id, product.Sku, product.Name, quantity, product.Price, product.ImageUrl));
        }

        var a = request.ShippingAddress;
        var order = Order.Place(userId, email, new ShippingAddress(a.FullName, a.Line1, a.Line2, a.City, a.PostalCode, a.Country.ToUpperInvariant()),
            request.PaymentMethod, items);

        _db.Orders.Add(order);
        _db.Enqueue(Topics.OrderPlaced, order.Id, EventTypes.OrderPlaced, new OrderPlaced(
            order.Id, order.UserId, order.Email,
            order.Items.Select(i => new OrderLineDto(i.ProductId, i.Sku, i.Name, i.Quantity, i.UnitPrice)).ToList(),
            order.Total, order.Currency, order.PaymentMethod));

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _db.IdempotencyRecords.Add(new IdempotencyRecord(userId, idempotencyKey, order.Id));
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Placed order {OrderNumber} ({OrderId}) total {Total}", order.Number, order.Id, order.Total);

        try
        {
            await _cart.ClearCartAsync(bearerToken, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Order {OrderId} placed but the cart could not be cleared", order.Id);
        }

        return new PlaceOrderResult(order, false, adjustments);
    }

    public async Task<Order> CancelByCustomerAsync(Guid orderId, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var order = await LoadAsync(orderId, ct, tracking: true);
        if (order.UserId != userId && !isAdmin)
        {
            throw new NotFoundException($"Order {orderId} not found.");
        }

        if (!order.CanBeCancelledByCustomer)
        {
            throw new ConflictException($"Order is {order.Status} and can no longer be cancelled.");
        }

        var reason = isAdmin && order.UserId != userId ? "Cancelled by an administrator" : "Cancelled by customer";
        order.Cancel(reason);
        _db.Enqueue(Topics.OrderCancelled, order.Id, EventTypes.OrderCancelled, new OrderCancelled(order.Id, order.UserId, order.Email, reason));
        await _db.SaveChangesAsync(ct);
        return order;
    }

    private async Task<Order> LoadAsync(Guid orderId, CancellationToken ct, bool tracking = false)
    {
        var query = _db.Orders.Include(o => o.Items).Include(o => o.History).AsSplitQuery();
        return await (tracking ? query : query.AsNoTracking()).SingleOrDefaultAsync(o => o.Id == orderId, ct)
               ?? throw new NotFoundException($"Order {orderId} not found.");
    }
}
