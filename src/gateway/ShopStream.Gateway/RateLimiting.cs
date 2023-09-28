using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ShopStream.Gateway;

public static class RateLimiting
{
    public const string PerIp = "per-ip";
    public const string Auth = "auth";

    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services)
    {
        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                }

                ctx.HttpContext.Response.ContentType = "application/problem+json";
                await ctx.HttpContext.Response.WriteAsync(
                    "{\"type\":\"https://tools.ietf.org/html/rfc6585#section-4\",\"title\":\"Too many requests\",\"status\":429}", ct);
            };

            // per client IP
            options.AddPolicy(PerIp, ctx => RateLimitPartition.GetFixedWindowLimiter(ClientKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Setting(ctx, "RateLimiting:PermitsPerWindow", 120),
                Window = TimeSpan.FromSeconds(10),
                QueueLimit = 0
            }));

            options.AddPolicy(Auth, ctx => RateLimitPartition.GetSlidingWindowLimiter(ClientKey(ctx), _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = Setting(ctx, "RateLimiting:AuthPermitsPerMinute", 10),
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0
            }));
        });
    }

    // read at request time so test overrides apply
    private static int Setting(HttpContext ctx, string key, int fallback) =>
        ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue(key, fallback);

    private static string ClientKey(HttpContext ctx) =>
        ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
        ?? ctx.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";
}
