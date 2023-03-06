using ShopStream.BuildingBlocks;
using ShopStream.Orders.Domain;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("orders", typeof(Program));

var app = builder.Build();

app.UseServiceDefaults();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.Run();

public partial class Program { }
