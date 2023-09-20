using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopStream.Contracts;
using ShopStream.Orders.Application;
using ShopStream.Orders.Clients;
using ShopStream.TestSupport;

namespace ShopStream.Orders.IntegrationTests;

public class OrdersApiTests : IClassFixture<OrdersApiFactory>
{
    private static readonly Guid Headphones = Guid.NewGuid();
    private static readonly Guid Book = Guid.NewGuid();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly OrdersApiFactory _factory;

    public OrdersApiTests(OrdersApiFactory factory)
    {
        _factory = factory;
        _factory.Catalog.Products[Headphones] = new CatalogProduct(Headphones, "ELE-1001", "Aurora Wireless Headphones", 129.99m, "USD", true, "https://img/1");
        _factory.Catalog.Products[Book] = new CatalogProduct(Book, "BOO-1002", "Night Garden", 16.99m, "USD", true, "https://img/2");
    }

    private (HttpClient Client, string Token, Guid UserId) Customer()
    {
        var userId = Guid.NewGuid();
        var token = TestTokens.Create(userId, $"{userId:N}@example.com");
        return (_factory.CreateClient().WithToken(token), token, userId);
    }

    private static object ValidRequest(string paymentMethod = "pm_card_visa") => new
    {
        shippingAddress = new { fullName = "Casey Customer", line1 = "1 Main St", city = "Springfield", postalCode = "12345", country = "us" },
        paymentMethod
    };

    [Fact]
    public async Task Requires_authentication()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/orders", ValidRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Places_order_with_catalog_prices_and_queues_order_placed()
    {
        var (client, token, userId) = Customer();
        _factory.Cart.Carts[token] = new() { new CartItem(Headphones, "Aurora Wireless Headphones", 1, 119.99m, null), new CartItem(Book, "Night Garden", 2, 16.99m, null) };

        var response = await client.PostAsJsonAsync("/api/orders", ValidRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = (await response.Content.ReadFromJsonAsync<OrderDetailDto>(Json))!;
        order.Status.Should().Be("Pending");
        order.Total.Should().Be(129.99m + 2 * 16.99m);
        order.ShippingAddress.Country.Should().Be("US");
        order.PriceAdjustments.Should().ContainSingle(a => a.ProductId == Headphones && a.CatalogPrice == 129.99m);
        order.Timeline.Should().ContainSingle().Which.To.Should().Be("Pending");
        _factory.Cart.Cleared.Should().Contain(token);

        var outbox = await _factory.WithDbAsync(db => db.OutboxMessages.AsNoTracking().Where(m => m.Key == order.Id.ToString()).ToListAsync());
        var message = outbox.Should().ContainSingle().Subject;
        message.Topic.Should().Be(Topics.OrderPlaced);
        var payload = JsonDocument.Parse(message.Payload).RootElement.GetProperty("payload");
        payload.GetProperty("userId").GetGuid().Should().Be(userId);
        payload.GetProperty("items").GetArrayLength().Should().Be(2);
        payload.GetProperty("paymentMethod").GetString().Should().Be("pm_card_visa");
    }

    [Fact]
    public async Task Same_idempotency_key_returns_the_original_order()
    {
        var (client, token, _) = Customer();
        _factory.Cart.Carts[token] = new() { new CartItem(Book, "Night Garden", 1, 16.99m, null) };
        client.DefaultRequestHeaders.Add("Idempotency-Key", "checkout-123");

        var first = await client.PostAsJsonAsync("/api/orders", ValidRequest());
        var second = await client.PostAsJsonAsync("/api/orders", ValidRequest());

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadFromJsonAsync<OrderDetailDto>(Json))!.Id
            .Should().Be((await first.Content.ReadFromJsonAsync<OrderDetailDto>(Json))!.Id);
    }

    [Fact]
    public async Task Empty_cart_is_a_bad_request()
    {
        var (client, _, _) = Customer();
        var response = await client.PostAsJsonAsync("/api/orders", ValidRequest());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync()).Should().Contain("cart is empty");
    }

    [Fact]
    public async Task Validation_errors_are_reported_per_field()
    {
        var (client, _, _) = Customer();
        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            shippingAddress = new { fullName = "", line1 = "x", city = "y", postalCode = "z", country = "USA" },
            paymentMethod = "4242424242424242"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("PaymentMethod").And.Contain("ShippingAddress.Country").And.Contain("ShippingAddress.FullName");
    }

    [Fact]
    public async Task Inactive_products_cannot_be_ordered()
    {
        var retired = Guid.NewGuid();
        _factory.Catalog.Products[retired] = new CatalogProduct(retired, "OLD-1", "Retired", 5m, "USD", false, "");
        var (client, token, _) = Customer();
        _factory.Cart.Carts[token] = new() { new CartItem(retired, "Retired", 1, 5m, null) };

        var response = await client.PostAsJsonAsync("/api/orders", ValidRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Orders_are_private_to_their_owner_but_visible_to_admins()
    {
        var (client, token, _) = Customer();
        _factory.Cart.Carts[token] = new() { new CartItem(Book, "Night Garden", 1, 16.99m, null) };
        var order = (await (await client.PostAsJsonAsync("/api/orders", ValidRequest())).Content.ReadFromJsonAsync<OrderDetailDto>(Json))!;

        var (stranger, _, _) = Customer();
        (await stranger.GetAsync($"/api/orders/{order.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var admin = _factory.CreateClient().WithToken(TestTokens.Create(Guid.NewGuid(), "admin@shopstream.local", Roles.Admin));
        (await admin.GetAsync($"/api/orders/{order.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var mine = await client.GetFromJsonAsync<JsonElement>("/api/orders");
        mine.GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Customer_can_cancel_a_pending_order_which_queues_compensation()
    {
        var (client, token, _) = Customer();
        _factory.Cart.Carts[token] = new() { new CartItem(Book, "Night Garden", 1, 16.99m, null) };
        var order = (await (await client.PostAsJsonAsync("/api/orders", ValidRequest())).Content.ReadFromJsonAsync<OrderDetailDto>(Json))!;

        var response = await client.PostAsync($"/api/orders/{order.Id}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelled = (await response.Content.ReadFromJsonAsync<OrderDetailDto>(Json))!;
        cancelled.Status.Should().Be("Cancelled");
        cancelled.CancellationReason.Should().Be("Cancelled by customer");
        var topics = await _factory.WithDbAsync(db => db.OutboxMessages.Where(m => m.Key == order.Id.ToString()).Select(m => m.Topic).ToListAsync());
        topics.Should().BeEquivalentTo(new[] { Topics.OrderPlaced, Topics.OrderCancelled });

        (await client.PostAsync($"/api/orders/{order.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Saga_events_move_the_order_to_confirmed()
    {
        var (client, token, userId) = Customer();
        _factory.Cart.Carts[token] = new() { new CartItem(Headphones, "Aurora Wireless Headphones", 1, 129.99m, null) };
        var order = (await (await client.PostAsJsonAsync("/api/orders", ValidRequest())).Content.ReadFromJsonAsync<OrderDetailDto>(Json))!;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var saga = scope.ServiceProvider.GetRequiredService<OrderSagaHandler>();
            await saga.HandleStockReservedAsync(Guid.NewGuid(), new StockReserved(order.Id, Guid.NewGuid(), userId, order.Email, order.Total, "USD", "pm_card_visa", DateTimeOffset.UtcNow.AddMinutes(15)), default);
            await saga.HandlePaymentSucceededAsync(Guid.NewGuid(), new PaymentSucceeded(order.Id, Guid.NewGuid(), order.Total, "USD", "mock", "mock_pi"), default);
        }

        var detail = (await client.GetFromJsonAsync<OrderDetailDto>($"/api/orders/{order.Id}", Json))!;
        detail.Status.Should().Be("Confirmed");
        detail.Timeline.Select(t => t.To).Should().Equal("Pending", "InventoryReserved", "Paid", "Confirmed");
        detail.CanCancel.Should().BeFalse();
    }

    [Fact]
    public async Task Admin_endpoints_filter_and_summarise()
    {
        var (client, token, _) = Customer();
        _factory.Cart.Carts[token] = new() { new CartItem(Book, "Night Garden", 1, 16.99m, null) };
        await client.PostAsJsonAsync("/api/orders", ValidRequest());

        (await client.GetAsync("/api/admin/orders")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var admin = _factory.CreateClient().WithToken(TestTokens.Create(Guid.NewGuid(), "admin@shopstream.local", Roles.Admin));
        var pending = await admin.GetFromJsonAsync<JsonElement>("/api/admin/orders?status=pending");
        pending.GetProperty("items").EnumerateArray().Should().OnlyContain(o => o.GetProperty("status").GetString() == "Pending");

        (await admin.GetAsync("/api/admin/orders?status=bogus")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var summary = await admin.GetFromJsonAsync<SalesSummaryDto>("/api/admin/sales-summary?days=7", Json);
        summary!.Daily.Should().HaveCount(7);
        summary.TotalOrders.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Details_composes_order_payment_and_current_product_snapshots()
    {
        var (client, token, _) = Customer();
        _factory.Cart.Carts[token] = new() { new CartItem(Book, "Night Garden", 2, 16.99m, null) };
        var order = (await (await client.PostAsJsonAsync("/api/orders", ValidRequest())).Content.ReadFromJsonAsync<OrderDetailDto>(Json))!;
        _factory.Payments.Payments[order.Id] = new PaymentInfo(Guid.NewGuid(), order.Id, order.Total, "USD", "mock", "Succeeded", null, "mock_pi_1", DateTimeOffset.UtcNow);
        _factory.Catalog.Products[Book] = _factory.Catalog.Products[Book] with { Price = 14.99m };

        var details = (await client.GetFromJsonAsync<OrderDetailsView>($"/api/orders/{order.Id}/details", Json))!;

        details.Order.Id.Should().Be(order.Id);
        details.Payment!.Status.Should().Be("Succeeded");
        var line = details.Products.Should().ContainSingle().Subject;
        line.OrderedUnitPrice.Should().Be(16.99m);
        line.CurrentPrice.Should().Be(14.99m);
        line.StillAvailable.Should().BeTrue();
        details.Warnings.Should().BeEmpty();
        _factory.Catalog.Products[Book] = _factory.Catalog.Products[Book] with { Price = 16.99m };
    }

    [Fact]
    public async Task Details_degrade_when_a_dependency_is_down()
    {
        var (client, token, _) = Customer();
        _factory.Cart.Carts[token] = new() { new CartItem(Book, "Night Garden", 1, 16.99m, null) };
        var order = (await (await client.PostAsJsonAsync("/api/orders", ValidRequest())).Content.ReadFromJsonAsync<OrderDetailDto>(Json))!;

        _factory.Payments.Down = true;
        try
        {
            var response = await client.GetAsync($"/api/orders/{order.Id}/details");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var details = (await response.Content.ReadFromJsonAsync<OrderDetailsView>(Json))!;
            details.Payment.Should().BeNull();
            details.Warnings.Should().ContainSingle(w => w.Contains("payments"));
            details.Products.Should().ContainSingle(p => p.CurrentName == "Night Garden");
        }
        finally
        {
            _factory.Payments.Down = false;
        }
    }

    [Fact]
    public async Task Demo_customer_jane_has_seeded_order_history()
    {
        var jane = _factory.CreateClient().WithToken(TestTokens.Create(DemoAccounts.JaneId, DemoAccounts.JaneEmail));
        var mine = await jane.GetFromJsonAsync<JsonElement>("/api/orders");

        mine.GetProperty("total").GetInt32().Should().Be(3);
        var statuses = mine.GetProperty("items").EnumerateArray().Select(o => o.GetProperty("status").GetString()).ToList();
        statuses.Should().BeEquivalentTo(new[] { "Confirmed", "Confirmed", "Cancelled" });

        var detail = await jane.GetFromJsonAsync<OrderDetailDto>($"/api/orders/{DemoAccounts.JaneOrder3}", Json);
        detail!.Timeline.Select(t => t.To).Should().Equal("Pending", "InventoryReserved", "Cancelled");
        detail.CancellationReason.Should().Contain("declined");

        // regression: owned addresses can't be shared between orders
        foreach (var id in new[] { DemoAccounts.JaneOrder1, DemoAccounts.JaneOrder2, DemoAccounts.JaneOrder3 })
        {
            (await jane.GetFromJsonAsync<OrderDetailDto>($"/api/orders/{id}", Json))!.ShippingAddress.FullName.Should().Be("Jane Doe");
        }
    }

    [Fact]
    public async Task Health_endpoints_respond()
    {
        var client = _factory.CreateClient();
        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Correlation_id_is_echoed()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "test-corr-42");
        var response = await client.GetAsync("/health");
        response.Headers.GetValues("X-Correlation-ID").Should().ContainSingle("test-corr-42");
    }
}
