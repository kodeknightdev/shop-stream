using Microsoft.EntityFrameworkCore;
using ShopStream.BuildingBlocks.Auth;
using ShopStream.BuildingBlocks.Errors;
using ShopStream.BuildingBlocks.Validation;
using ShopStream.Identity.Data;

namespace ShopStream.Identity.Endpoints;

public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization(AuthPolicies.AdminOnly);

        group.MapGet("/", async (IdentityDbContext db, string? search, int? page, int? pageSize, CancellationToken ct) =>
        {
            var p = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 20, 1, 100);
            var query = db.Users.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLowerInvariant();
                query = query.Where(u => u.Email.Contains(term) || u.FullName.ToLower().Contains(term));
            }

            var total = await query.CountAsync(ct);
            var items = await query.OrderBy(u => u.Email).Skip((p - 1) * size).Take(size).ToListAsync(ct);
            return Results.Ok(new { items = items.Select(AuthEndpoints.ToDto), total, page = p, pageSize = size });
        });

        group.MapPut("/{id:guid}/role", async (Guid id, ChangeRoleRequest request, IdentityDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException($"User {id} not found.");
            user.ChangeRole(request.Role);
            await db.SaveChangesAsync(ct);
            return Results.Ok(AuthEndpoints.ToDto(user));
        }).Validate<ChangeRoleRequest>();

        return group;
    }
}
