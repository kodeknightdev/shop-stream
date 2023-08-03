using ShopStream.BuildingBlocks.Auth;
using ShopStream.BuildingBlocks.Correlation;
using ShopStream.BuildingBlocks.Errors;
using ShopStream.BuildingBlocks.Health;
using ShopStream.BuildingBlocks.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.AddShopStreamLogging("gateway");
builder.Services.AddShopStreamProblemDetails();
builder.Services.AddShopStreamJwt(builder.Configuration);
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddHealthChecks();

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
app.UseAuthentication();
app.UseAuthorization();

app.MapShopStreamHealth();
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapReverseProxy();

app.Run();

public partial class Program { }
