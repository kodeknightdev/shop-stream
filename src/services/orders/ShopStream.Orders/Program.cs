using Microsoft.EntityFrameworkCore;
using ShopStream.BuildingBlocks;
using ShopStream.Orders.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("orders", typeof(Program));
builder.Services.AddDbContext<OrdersDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("OrdersDb")));

var app = builder.Build();

app.UseServiceDefaults();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.Run();

public partial class Program { }
