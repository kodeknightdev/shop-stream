using Microsoft.EntityFrameworkCore;
using ShopStream.BuildingBlocks;
using ShopStream.BuildingBlocks.Auth;
using ShopStream.BuildingBlocks.Health;
using ShopStream.Identity.Auth;
using ShopStream.Identity.Data;
using ShopStream.Identity.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("identity", typeof(RegisterRequestValidator));

builder.Services.AddDbContext<IdentityDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("IdentityDb")));
builder.Services.AddHealthChecks().AddDbContextCheck<IdentityDbContext>("postgres", tags: new[] { HealthEndpoints.ReadyTag });

builder.Services.AddShopStreamJwt(builder.Configuration);
builder.Services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
builder.Services.AddSingleton<ITokenService, TokenService>();

var app = builder.Build();

app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();

app.MapShopStreamHealth();
app.MapAuthEndpoints();

app.Run();

public partial class Program { }
