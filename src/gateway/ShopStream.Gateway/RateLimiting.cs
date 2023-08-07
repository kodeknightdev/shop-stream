using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ShopStream.Gateway;

public static class RateLimiting
{
    public const string PerIp = "per-ip";
    public const string Auth = "auth";

    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var permits = configuration.GetValue("RateLimiting:PermitsPerWindow", 120);
        var authPermits = configuration.GetValue("RateLimiting:AuthPermitsPerMinute", 10);

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
                PermitLimit = permits,
                Window = TimeSpan.FromSeconds(10),
                QueueLimit = 0
            }));

            options.AddPolicy(Auth, ctx => RateLimitPartition.GetSlidingWindowLimiter(ClientKey(ctx), _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = authPermits,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0
            }));
        });
    }

    private static string ClientKey(HttpContext ctx) =>
        ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
        ?? ctx.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";
}
