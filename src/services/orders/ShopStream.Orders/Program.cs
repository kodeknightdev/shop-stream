using Microsoft.EntityFrameworkCore;
using ShopStream.BuildingBlocks;
using ShopStream.BuildingBlocks.Auth;
using ShopStream.BuildingBlocks.Correlation;
using ShopStream.BuildingBlocks.Health;
using ShopStream.BuildingBlocks.Kafka;
using ShopStream.BuildingBlocks.Messaging;
using ShopStream.Orders.Application;
using ShopStream.Orders.Clients;
using ShopStream.Orders.Data;
using ShopStream.Orders.Endpoints;
using ShopStream.Orders.Messaging;
using ShopStream.Protos.Catalog;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("orders", typeof(PlaceOrderRequestValidator));

builder.Services.AddDbContext<OrdersDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("OrdersDb")));
builder.Services.AddHealthChecks().AddDbContextCheck<OrdersDbContext>("postgres", tags: new[] { HealthEndpoints.ReadyTag });
builder.Services.AddShopStreamJwt(builder.Configuration);
builder.Services.AddShopStreamKafka(builder.Configuration);

builder.Services.AddScoped<PlaceOrderService>();
builder.Services.AddScoped<OrderSagaHandler>();
builder.Services.AddScoped<OrderDetailsComposer>();
builder.Services.AddOutboxRelay<OrdersDbContext>(builder.Configuration);
builder.Services.AddHostedService<OrderEventsConsumer>();

// all outgoing calls go through ResiliencePolicies
var resilience = builder.Configuration.GetSection(ResilienceOptions.SectionName).Get<ResilienceOptions>() ?? new ResilienceOptions();
using var resilienceLogs = LoggerFactory.Create(l => l.AddConsole());

builder.Services
    .AddGrpcClient<CatalogService.CatalogServiceClient>(o => o.Address = new Uri(builder.Configuration["Services:CatalogGrpc"]!))
    .AddHttpMessageHandler<CorrelationIdDelegatingHandler>();
builder.Services.AddSingleton(new CatalogResilience(ResiliencePolicies.ForGrpc("catalog", resilience, resilienceLogs.CreateLogger("Resilience.Catalog"))));
builder.Services.AddScoped<ICatalogClient, GrpcCatalogClient>();

builder.Services
    .AddHttpClient<ICartClient, HttpCartClient>(c => c.BaseAddress = new Uri(builder.Configuration["Services:Cart"]!))
    .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
    .AddPolicyHandler(ResiliencePolicies.ForHttp("cart", resilience, resilienceLogs.CreateLogger("Resilience.Cart")));

builder.Services
    .AddHttpClient<IPaymentsClient, HttpPaymentsClient>(c => c.BaseAddress = new Uri(builder.Configuration["Services:Payments"]!))
    .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
    .AddPolicyHandler(ResiliencePolicies.ForHttp("payments", resilience, resilienceLogs.CreateLogger("Resilience.Payments")));

var app = builder.Build();

app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();

app.MapShopStreamHealth();
app.MapOrderEndpoints();
app.MapAdminEndpoints();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    if (db.Database.IsRelational())
    {
        await db.Database.MigrateAsync();
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }

    if (app.Environment.IsDevelopment())
    {
        await DemoOrderSeeder.SeedAsync(db, app.Logger);
    }
}

app.Run();

public partial class Program { }
