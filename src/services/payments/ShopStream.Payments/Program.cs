using Microsoft.EntityFrameworkCore;
using ShopStream.BuildingBlocks;
using ShopStream.Payments.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("payments", typeof(Program));
builder.Services.AddDbContext<PaymentsDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("PaymentsDb")));

var app = builder.Build();

app.UseServiceDefaults();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.Run();

public partial class Program { }
