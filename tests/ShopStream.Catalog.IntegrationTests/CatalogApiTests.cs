using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grpc.Net.Client;
using ShopStream.BuildingBlocks.Kafka;
using ShopStream.Catalog.Data;
using ShopStream.Catalog.Endpoints;
using ShopStream.Contracts;
using ShopStream.Protos.Catalog;
using ShopStream.TestSupport;

namespace ShopStream.Catalog.IntegrationTests;

public sealed class CatalogApiFactory : ServiceFactory<Program, CatalogDbContext> { }

public class CatalogApiTests : IClassFixture<CatalogApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly CatalogApiFactory _factory;
    private readonly HttpClient _client;

    public CatalogApiTests(CatalogApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private HttpClient Admin() => _factory.CreateClient().WithToken(TestTokens.Create(Guid.NewGuid(), "admin@shopstream.local", Roles.Admin));

    [Fact]
    public async Task Seeds_six_categories_and_forty_products()
    {
        var categories = await _client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", Json);
        categories.Should().HaveCount(6);
        categories!.Sum(c => c.ProductCount).Should().Be(40);
    }

    [Fact]
    public async Task Paginates()
    {
        var page = await _client.GetFromJsonAsync<PagedResult<ProductSummaryDto>>("/api/products?page=2&pageSize=15", Json);
        page!.Total.Should().Be(40);
        page.Items.Should().HaveCount(15);
        page.Page.Should().Be(2);
        page.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task Filters_by_category_slug_and_price_range()
    {
        var page = await _client.GetFromJsonAsync<PagedResult<ProductSummaryDto>>("/api/products?category=books&minPrice=20&maxPrice=50&pageSize=60", Json);
        page!.Items.Should().NotBeEmpty();
        page.Items.Should().OnlyContain(p => p.CategoryName == "Books" && p.Price >= 20 && p.Price <= 50);
    }

    [Fact]
    public async Task Searches_name_and_sorts_by_price()
    {
        var page = await _client.GetFromJsonAsync<PagedResult<ProductSummaryDto>>("/api/products?search=set&sort=price_desc&pageSize=60", Json);
        page!.Items.Should().NotBeEmpty().And.OnlyContain(p => p.Name.Contains("Set", StringComparison.OrdinalIgnoreCase));
        page.Items.Select(p => p.Price).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task Rejects_inverted_price_range()
    {
        var response = await _client.GetAsync("/api/products?minPrice=50&maxPrice=10");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Product_detail_by_id_and_slug()
    {
        var seed = SeedData.Products[0];
        var byId = await _client.GetFromJsonAsync<ProductDetailDto>($"/api/products/{seed.Id}", Json);
        byId!.Sku.Should().Be(seed.Sku);
        byId.Attributes.Should().ContainKey("brand");

        var bySlug = await _client.GetFromJsonAsync<ProductDetailDto>($"/api/products/by-slug/{seed.Slug}", Json);
        bySlug!.Id.Should().Be(seed.Id);

        (await _client.GetAsync($"/api/products/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Product_reads_are_cached_and_invalidated_on_update()
    {
        var admin = Admin();
        var created = (await (await admin.PostAsJsonAsync("/api/products", NewProduct("TST-0100") with { Name = "Cache Lamp" }))
            .Content.ReadFromJsonAsync<ProductDetailDto>(Json))!;

        var first = await _client.GetAsync($"/api/products/{created.Id}");
        first.Headers.GetValues("X-Cache").Should().ContainSingle("MISS");
        var second = await _client.GetAsync($"/api/products/{created.Id}");
        second.Headers.GetValues("X-Cache").Should().ContainSingle("HIT");
        (await _client.GetAsync($"/api/products/by-slug/{created.Slug}")).Headers.GetValues("X-Cache").Should().ContainSingle("HIT");

        await admin.PutAsJsonAsync($"/api/products/{created.Id}", NewProduct("TST-0100") with { Name = "Cache Lamp", Price = 12.34m });

        var afterUpdate = await _client.GetAsync($"/api/products/{created.Id}");
        afterUpdate.Headers.GetValues("X-Cache").Should().ContainSingle("MISS");
        (await afterUpdate.Content.ReadFromJsonAsync<ProductDetailDto>(Json))!.Price.Should().Be(12.34m);

        await admin.DeleteAsync($"/api/products/{created.Id}");
        (await _client.GetAsync($"/api/products/{created.Id}")).Headers.GetValues("X-Cache").Should().ContainSingle("MISS");
    }

    [Fact]
    public async Task Writes_require_admin()
    {
        var request = NewProduct("TST-0001");
        (await _client.PostAsJsonAsync("/api/products", request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var customer = _factory.CreateClient().WithToken(TestTokens.Create(Guid.NewGuid(), "c@example.com"));
        (await customer.PostAsJsonAsync("/api/products", request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_crud_round_trip()
    {
        var admin = Admin();
        var created = await admin.PostAsJsonAsync("/api/products", NewProduct("TST-0002"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = (await created.Content.ReadFromJsonAsync<ProductDetailDto>(Json))!;
        product.Slug.Should().Be("test-lamp");

        var update = NewProduct("TST-0002") with { Price = 59.5m, Name = "Test Lamp Deluxe" };
        var updated = (await (await admin.PutAsJsonAsync($"/api/products/{product.Id}", update)).Content.ReadFromJsonAsync<ProductDetailDto>(Json))!;
        updated.Price.Should().Be(59.5m);
        updated.Slug.Should().Be("test-lamp-deluxe");

        (await admin.DeleteAsync($"/api/products/{product.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var hidden = await _client.GetFromJsonAsync<PagedResult<ProductSummaryDto>>("/api/products?search=Test%20Lamp", Json);
        hidden!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_sku_conflicts_and_validation_fails_with_problem_details()
    {
        var admin = Admin();
        var duplicate = await admin.PostAsJsonAsync("/api/products", NewProduct(SeedData.Products[1].Sku));
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var invalid = await admin.PostAsJsonAsync("/api/products", NewProduct("bad sku") with { Price = -1, ImageUrl = "nope" });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().Contain(new[] { "Sku", "Price", "ImageUrl" });
    }

    [Fact]
    public async Task Grpc_returns_authoritative_prices_and_missing_ids()
    {
        var channel = GrpcChannel.ForAddress(_factory.Server.BaseAddress, new GrpcChannelOptions { HttpHandler = _factory.Server.CreateHandler() });
        var client = new CatalogService.CatalogServiceClient(channel);
        var known = SeedData.Products[2];
        var missing = Guid.NewGuid().ToString();

        var request = new GetProductsByIdsRequest();
        request.ProductIds.Add(known.Id.ToString());
        request.ProductIds.Add(missing);
        var reply = await client.GetProductsByIdsAsync(request);

        reply.Products.Should().ContainSingle().Which.PriceCents.Should().Be((long)(known.Price * 100));
        reply.MissingIds.Should().ContainSingle(missing);
    }

    [Fact]
    public void Envelope_serialisation_is_camel_case()
    {
        var json = KafkaEventPublisher.Serialize(EventTypes.ProductPriceChanged, new ProductPriceChanged(Guid.Empty, "X", 1, 2), null);
        json.Should().Contain("\"newPrice\":2").And.Contain("\"eventType\":\"ProductPriceChanged\"");
    }

    private static UpsertProductRequest NewProduct(string sku) => new(
        SeedData.Categories[1].Id, sku, "Test Lamp", "A lamp used by the test suite", 49.99m, "https://picsum.photos/seed/lamp/640/480",
        new Dictionary<string, string> { ["color"] = "Brass" });
}
