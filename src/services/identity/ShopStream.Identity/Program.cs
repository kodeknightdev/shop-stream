using Microsoft.EntityFrameworkCore;
using ShopStream.BuildingBlocks;
using ShopStream.Identity.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("identity", typeof(Program));
builder.Services.AddDbContext<IdentityDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("IdentityDb")));

var app = builder.Build();

app.UseServiceDefaults();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.Run();

public partial class Program { }
