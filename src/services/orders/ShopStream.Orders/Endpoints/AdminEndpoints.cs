using Microsoft.EntityFrameworkCore;
using ShopStream.BuildingBlocks.Auth;
using ShopStream.Orders.Application;
using ShopStream.Orders.Data;
using ShopStream.Orders.Domain;

namespace ShopStream.Orders.Endpoints;

public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization(AuthPolicies.AdminOnly);

        group.MapGet("/orders", async (OrdersDbContext db, string? status, string? email, DateTimeOffset? from, DateTimeOffset? to,
            int? page, int? pageSize, CancellationToken ct) =>
        {
            var query = db.Orders.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<OrderStatus>(status, true, out var parsed))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["status"] = new[] { $"Unknown status. Use one of: {string.Join(", ", Enum.GetNames<OrderStatus>())}" }
                    });
                }

                query = query.Where(o => o.Status == parsed);
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                var term = email.Trim().ToLowerInvariant();
                query = query.Where(o => o.Email.Contains(term));
            }

            if (from is not null)
            {
                query = query.Where(o => o.CreatedAt >= from);
            }

            if (to is not null)
            {
                query = query.Where(o => o.CreatedAt <= to);
            }

            var p = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 20, 1, 100);
            var total = await query.CountAsync(ct);
            var orders = await query.OrderByDescending(o => o.CreatedAt).Skip((p - 1) * size).Take(size).Include(o => o.Items).ToListAsync(ct);
            return Results.Ok(new { items = orders.Select(o => o.ToSummary()), page = p, pageSize = size, total });
        });

        group.MapGet("/sales-summary", async (OrdersDbContext db, int? days, CancellationToken ct) =>
        {
            var window = Math.Clamp(days ?? 14, 1, 90);
            var since = DateTimeOffset.UtcNow.Date.AddDays(-(window - 1));
            var orders = await db.Orders.AsNoTracking().Where(o => o.CreatedAt >= since)
                .Select(o => new { o.CreatedAt, o.Status, o.Total }).ToListAsync(ct);

            return Results.Ok(SalesSummary.Build(window, DateOnly.FromDateTime(since), orders.Select(o => (o.CreatedAt, o.Status, o.Total))));
        }).Produces<SalesSummaryDto>();

        return group;
    }
}
