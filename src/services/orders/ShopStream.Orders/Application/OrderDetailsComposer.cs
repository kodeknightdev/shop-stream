using Microsoft.EntityFrameworkCore;
using ShopStream.BuildingBlocks.Errors;
using ShopStream.Orders.Clients;
using ShopStream.Orders.Data;

namespace ShopStream.Orders.Application;

public sealed record ProductSnapshotDto(Guid ProductId, string OrderedName, decimal OrderedUnitPrice, int Quantity,
    string? CurrentName, decimal? CurrentPrice, string? ImageUrl, bool? StillAvailable);

public sealed record OrderDetailsView(OrderDetailDto Order, PaymentInfo? Payment, IReadOnlyList<ProductSnapshotDto> Products, IReadOnlyList<string> Warnings);

// order from our db + payment and current products fetched in parallel; a failing dependency
// turns into a warning instead of an error
public sealed class OrderDetailsComposer
{
    private readonly OrdersDbContext _db;
    private readonly IPaymentsClient _payments;
    private readonly ICatalogClient _catalog;
    private readonly ILogger<OrderDetailsComposer> _logger;

    public OrderDetailsComposer(OrdersDbContext db, IPaymentsClient payments, ICatalogClient catalog, ILogger<OrderDetailsComposer> logger)
    {
        _db = db;
        _payments = payments;
        _catalog = catalog;
        _logger = logger;
    }

    public async Task<OrderDetailsView> ComposeAsync(Guid orderId, Guid userId, bool isAdmin, string bearerToken, CancellationToken ct)
    {
        var order = await _db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.History).AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null || (order.UserId != userId && !isAdmin))
        {
            throw new NotFoundException($"Order {orderId} not found.");
        }

        var warnings = new List<string>();
        var paymentTask = TryAsync("payments", () => _payments.GetForOrderAsync(orderId, bearerToken, ct), warnings);
        var productsTask = TryAsync<IReadOnlyDictionary<Guid, CatalogProduct>>("catalog",
            async () => await _catalog.GetProductsAsync(order.Items.Select(i => i.ProductId).Distinct(), ct), warnings);
        await Task.WhenAll(paymentTask, productsTask);

        var current = productsTask.Result;
        var products = order.Items.Select(i =>
        {
            var p = current?.GetValueOrDefault(i.ProductId);
            return new ProductSnapshotDto(i.ProductId, i.Name, i.UnitPrice, i.Quantity, p?.Name, p?.Price, p?.ImageUrl ?? i.ImageUrl, p?.IsActive);
        }).ToList();

        return new OrderDetailsView(order.ToDetail(), paymentTask.Result, products, warnings);
    }

    private async Task<T?> TryAsync<T>(string dependency, Func<Task<T?>> call, List<string> warnings) where T : class
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (ResiliencePolicies.IsDependencyFailure(ex))
        {
            _logger.LogWarning(ex, "Order details composed without {Dependency}", dependency);
            lock (warnings)
            {
                warnings.Add($"{dependency} is unavailable; some details are missing.");
            }

            return null;
        }
    }
}
