using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ShopStream.BuildingBlocks.Auth;
using ShopStream.BuildingBlocks.Errors;
using ShopStream.Payments.Data;
using ShopStream.Payments.Domain;
using ShopStream.Payments.Providers;

namespace ShopStream.Payments.Endpoints;

public sealed record PaymentDto(Guid Id, Guid OrderId, decimal Amount, string Currency, string Provider, string Status, string? FailureReason,
    string? ProviderReference, DateTimeOffset CreatedAt);

public static class PaymentEndpoints
{
    public static RouteGroupBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/payments").WithTags("Payments").RequireAuthorization(AuthPolicies.Authenticated);

        group.MapGet("/orders/{orderId:guid}", async (Guid orderId, ClaimsPrincipal user, PaymentsDbContext db, CancellationToken ct) =>
        {
            var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.OrderId == orderId, ct);
            if (payment is null || (payment.UserId != user.GetUserId() && !user.IsAdmin()))
            {
                throw new NotFoundException($"No payment for order {orderId}.");
            }

            return Results.Ok(ToDto(payment));
        }).Produces<PaymentDto>();

        group.MapGet("", async (PaymentsDbContext db, string? status, int? page, int? pageSize, CancellationToken ct) =>
        {
            var query = db.Payments.AsNoTracking();
            if (Enum.TryParse<PaymentStatus>(status, true, out var parsed))
            {
                query = query.Where(p => p.Status == parsed);
            }

            var p = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 20, 1, 100);
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(x => x.CreatedAt).Skip((p - 1) * size).Take(size).ToListAsync(ct);
            return Results.Ok(new { items = items.Select(ToDto), page = p, pageSize = size, total });
        }).RequireAuthorization(AuthPolicies.AdminOnly);

        group.MapGet("/provider", (IPaymentProvider provider) => Results.Ok(new { provider = provider.Name }))
            .AllowAnonymous();

        return group;
    }

    private static PaymentDto ToDto(Payment p) =>
        new(p.Id, p.OrderId, p.Amount, p.Currency, p.Provider, p.Status.ToString(), p.FailureReason, p.ProviderReference, p.CreatedAt);
}
