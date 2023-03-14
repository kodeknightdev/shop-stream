using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ShopStream.BuildingBlocks.Auth;
using ShopStream.BuildingBlocks.Errors;
using ShopStream.BuildingBlocks.Validation;
using ShopStream.Contracts;
using ShopStream.Orders.Application;
using ShopStream.Orders.Data;

namespace ShopStream.Orders.Endpoints;

public static class OrderEndpoints
{
    public static RouteGroupBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders").WithTags("Orders").RequireAuthorization(AuthPolicies.Authenticated);

        group.MapPost("", PlaceOrder).Validate<PlaceOrderRequest>()
            .Produces<OrderDetailDto>(201).Produces<OrderDetailDto>().ProducesProblem(400).ProducesProblem(409).ProducesProblem(503);
        group.MapGet("", MyOrders).Produces<object>();
        group.MapGet("/{id:guid}", GetOrder).Produces<OrderDetailDto>().ProducesProblem(404);
        group.MapPost("/{id:guid}/cancel", Cancel).Produces<OrderDetailDto>().ProducesProblem(409);

        return group;
    }

    private static string BearerToken(HttpContext http)
    {
        var token = http.Request.Headers.Authorization.ToString();
        return token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token["Bearer ".Length..] : token;
    }

    private static async Task<IResult> PlaceOrder(PlaceOrderRequest request, ClaimsPrincipal user, HttpContext http, PlaceOrderService service, CancellationToken ct)
    {
        var token = BearerToken(http);
        var key = http.Request.Headers[HttpHeaderNames.IdempotencyKey].ToString();
        if (key.Length > 100)
        {
            throw new BadRequestException("Idempotency-Key must be at most 100 characters.");
        }

        var result = await service.PlaceAsync(user.GetUserId(), user.GetEmail(), token, request, string.IsNullOrWhiteSpace(key) ? null : key, ct);
        var dto = result.Order.ToDetail(result.PriceAdjustments);
        return result.Replayed ? Results.Ok(dto) : Results.Created($"/api/orders/{result.Order.Id}", dto);
    }

    private static async Task<IResult> MyOrders(ClaimsPrincipal user, OrdersDbContext db, int? page, int? pageSize, CancellationToken ct)
    {
        var userId = user.GetUserId();
        var p = Math.Max(1, page ?? 1);
        var size = Math.Clamp(pageSize ?? 10, 1, 50);
        var query = db.Orders.AsNoTracking().Where(o => o.UserId == userId);
        var total = await query.CountAsync(ct);
        var orders = await query.OrderByDescending(o => o.CreatedAt).Skip((p - 1) * size).Take(size).Include(o => o.Items).ToListAsync(ct);
        return Results.Ok(new { items = orders.Select(o => o.ToSummary()), page = p, pageSize = size, total });
    }

    private static async Task<IResult> GetOrder(Guid id, ClaimsPrincipal user, OrdersDbContext db, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.History).AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Id == id, ct);
        if (order is null || (order.UserId != user.GetUserId() && !user.IsAdmin()))
        {
            throw new NotFoundException($"Order {id} not found.");
        }

        return Results.Ok(order.ToDetail());
    }

    private static async Task<IResult> Cancel(Guid id, ClaimsPrincipal user, PlaceOrderService service, CancellationToken ct)
    {
        var order = await service.CancelByCustomerAsync(id, user.GetUserId(), user.IsAdmin(), ct);
        return Results.Ok(order.ToDetail());
    }
}
