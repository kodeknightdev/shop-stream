using Microsoft.EntityFrameworkCore;
using ShopStream.BuildingBlocks;
using ShopStream.BuildingBlocks.Auth;
using ShopStream.BuildingBlocks.Health;
using ShopStream.BuildingBlocks.Kafka;
using ShopStream.BuildingBlocks.Messaging;
using ShopStream.Payments.Application;
using ShopStream.Payments.Data;
using ShopStream.Payments.Endpoints;
using ShopStream.Payments.Messaging;
using ShopStream.Payments.Providers;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("payments", typeof(PaymentProcessor));

builder.Services.AddDbContext<PaymentsDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("PaymentsDb")));
builder.Services.AddHealthChecks().AddDbContextCheck<PaymentsDbContext>("postgres", tags: new[] { HealthEndpoints.ReadyTag });
builder.Services.AddShopStreamJwt(builder.Configuration);
builder.Services.AddShopStreamKafka(builder.Configuration);

using (var loggerFactory = LoggerFactory.Create(l => l.AddConsole()))
{
    builder.Services.AddPaymentProvider(builder.Configuration, loggerFactory.CreateLogger("Payments"));
}

builder.Services.AddScoped<PaymentProcessor>();
builder.Services.AddOutboxRelay<PaymentsDbContext>(builder.Configuration);
builder.Services.AddHostedService<PaymentsConsumer>();

var app = builder.Build();

app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();

app.MapShopStreamHealth();
app.MapPaymentEndpoints();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
    if (db.Database.IsRelational())
    {
        await db.Database.MigrateAsync();
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }
}

app.Run();

public partial class Program { }
