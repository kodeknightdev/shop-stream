using Microsoft.EntityFrameworkCore;
using ShopStream.BuildingBlocks;
using ShopStream.BuildingBlocks.Auth;
using ShopStream.BuildingBlocks.Health;
using ShopStream.Catalog.Data;
using ShopStream.Catalog.Endpoints;
using ShopStream.Catalog.Grpc;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("catalog", typeof(UpsertProductRequestValidator));

builder.Services.AddDbContext<CatalogDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("CatalogDb")));
builder.Services.AddHealthChecks().AddDbContextCheck<CatalogDbContext>("postgres", tags: new[] { HealthEndpoints.ReadyTag });
builder.Services.AddShopStreamJwt(builder.Configuration);
builder.Services.AddGrpc(o => o.EnableDetailedErrors = builder.Environment.IsDevelopment());
builder.Services.AddGrpcReflection();

var app = builder.Build();

app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();

app.MapShopStreamHealth();
app.MapCategoryEndpoints();
app.MapProductEndpoints();
app.MapGrpcService<CatalogGrpcService>();
if (app.Environment.IsDevelopment())
{
    app.MapGrpcReflectionService();
}

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await DbSeeder.MigrateAndSeedAsync(app.Services, app.Logger, seedDemoData: app.Environment.IsDevelopment());
}

app.Run();

public partial class Program { }
