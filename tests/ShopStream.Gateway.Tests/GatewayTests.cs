using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using ShopStream.Contracts;
using ShopStream.TestSupport;

namespace ShopStream.Gateway.Tests;

public sealed class GatewayFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = TestTokens.SigningKey,
            ["RateLimiting:AuthPermitsPerMinute"] = "3",
            // nothing listens here, so proxied calls fail fast with 502
            ["ReverseProxy:Clusters:orders:Destinations:primary:Address"] = "http://127.0.0.1:1",
            ["ReverseProxy:Clusters:identity:Destinations:primary:Address"] = "http://127.0.0.1:1"
        }));
    }
}

public class GatewayTests : IClassFixture<GatewayFactory>
{
    private readonly GatewayFactory _factory;

    public GatewayTests(GatewayFactory factory) => _factory = factory;

    [Fact]
    public async Task Starts_and_serves_health_and_swagger()
    {
        var client = _factory.CreateClient();
        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/swagger/index.html")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/api/orders")]
    [InlineData("/api/cart")]
    [InlineData("/api/admin/sales-summary")]
    public async Task Protected_routes_require_a_token(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_routes_reject_customers()
    {
        var client = _factory.CreateClient().WithToken(TestTokens.Create(Guid.NewGuid(), "c@example.com"));
        (await client.GetAsync("/api/admin/orders")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Authorised_requests_are_proxied()
    {
        var client = _factory.CreateClient().WithToken(TestTokens.Create(Guid.NewGuid(), "c@example.com"));
        var response = await client.GetAsync("/api/orders");
        // passes auth and reaches YARP, which can't connect to the fake destination
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        response.Headers.Contains("X-Correlation-ID").Should().BeTrue();
    }

    [Fact]
    public async Task Login_is_rate_limited()
    {
        var client = _factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            statuses.Add((await client.PostAsync("/api/auth/login", new StringContent("{}"))).StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests);
    }
}
