using ShopStream.BuildingBlocks.Auth;
using ShopStream.BuildingBlocks.Correlation;
using ShopStream.BuildingBlocks.Errors;
using ShopStream.BuildingBlocks.Health;
using ShopStream.BuildingBlocks.Logging;
using ShopStream.Gateway;

var builder = WebApplication.CreateBuilder(args);

builder.AddShopStreamLogging("gateway");
builder.Services.AddShopStreamProblemDetails();
builder.Services.AddShopStreamJwt(builder.Configuration);
builder.Services.AddGatewayRateLimiting();
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddHttpClient("downstream-health", c => c.Timeout = TimeSpan.FromSeconds(2));
builder.Services.AddHealthChecks().AddCheck<DownstreamHealthCheck>("downstream", tags: new[] { HealthEndpoints.ReadyTag });

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders(ShopStream.Contracts.HttpHeaderNames.CorrelationId)));

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseShopStreamExceptionHandler();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// one swagger UI, the docs are proxied through the docs-* routes
app.UseSwaggerUI(o =>
{
    o.RoutePrefix = "swagger";
    o.DocumentTitle = "shop-stream API";
    o.SwaggerEndpoint("/docs/identity/swagger.json", "identity");
    o.SwaggerEndpoint("/docs/catalog/swagger.json", "catalog");
    o.SwaggerEndpoint("/docs/orders/swagger.json", "orders");
    o.SwaggerEndpoint("/docs/payments/swagger.json", "payments");
    o.SwaggerEndpoint("/docs/inventory/openapi.json", "inventory");
    o.SwaggerEndpoint("/docs/cart/openapi.json", "cart");
    o.SwaggerEndpoint("/docs/email/openapi.json", "email");
});

app.MapShopStreamHealth();
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapReverseProxy();

app.Run();

public partial class Program { }
