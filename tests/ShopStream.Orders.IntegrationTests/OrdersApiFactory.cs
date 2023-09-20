using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ShopStream.Orders.Clients;
using ShopStream.Orders.Data;
using ShopStream.BuildingBlocks.Messaging;
using ShopStream.TestSupport;

namespace ShopStream.Orders.IntegrationTests;

public sealed class FakeCart : ICartClient
{
    public Dictionary<string, List<CartItem>> Carts { get; } = new();
    public List<string> Cleared { get; } = new();

    public Task<Cart?> GetCartAsync(string bearerToken, CancellationToken ct) =>
        Task.FromResult<Cart?>(Carts.TryGetValue(bearerToken, out var items) ? new Cart("u", items) : new Cart("u", new List<CartItem>()));

    public Task ClearCartAsync(string bearerToken, CancellationToken ct)
    {
        Cleared.Add(bearerToken);
        Carts.Remove(bearerToken);
        return Task.CompletedTask;
    }
}

public sealed class FakeCatalog : ICatalogClient
{
    public Dictionary<Guid, CatalogProduct> Products { get; } = new();

    public Task<IReadOnlyDictionary<Guid, CatalogProduct>> GetProductsAsync(IEnumerable<Guid> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, CatalogProduct>>(ids.Where(Products.ContainsKey).ToDictionary(i => i, i => Products[i]));
}

public sealed class FakePayments : IPaymentsClient
{
    public Dictionary<Guid, PaymentInfo> Payments { get; } = new();
    public bool Down { get; set; }

    public Task<PaymentInfo?> GetForOrderAsync(Guid orderId, string bearerToken, CancellationToken ct) =>
        Down ? throw new HttpRequestException("payments is down") : Task.FromResult(Payments.GetValueOrDefault(orderId));
}

public sealed class OrdersApiFactory : ServiceFactory<Program, OrdersDbContext>
{
    public FakeCart Cart { get; } = new();
    public FakeCatalog Catalog { get; } = new();
    public FakePayments Payments { get; } = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<ICartClient>();
        services.RemoveAll<ICatalogClient>();
        services.RemoveAll<IPaymentsClient>();
        services.AddSingleton<ICartClient>(Cart);
        services.AddSingleton<ICatalogClient>(Catalog);
        services.AddSingleton<IPaymentsClient>(Payments);

        // keep outbox rows so the tests can check them
        var relay = services.Single(d => d.ImplementationType == typeof(OutboxRelay<OrdersDbContext>));
        services.Remove(relay);
    }
}
