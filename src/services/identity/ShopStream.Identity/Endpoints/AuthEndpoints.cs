using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ShopStream.BuildingBlocks.Auth;
using ShopStream.BuildingBlocks.Errors;
using ShopStream.BuildingBlocks.Validation;
using ShopStream.Contracts;
using ShopStream.Identity.Auth;
using ShopStream.Identity.Data;
using ShopStream.Identity.Domain;

namespace ShopStream.Identity.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", Register).Validate<RegisterRequest>().Produces<AuthResponse>(201).ProducesProblem(409);
        group.MapPost("/login", Login).Validate<LoginRequest>().Produces<AuthResponse>().ProducesProblem(401);
        group.MapPost("/refresh", Refresh).Validate<RefreshRequest>().Produces<AuthResponse>().ProducesProblem(401);
        group.MapPost("/logout", Logout).Validate<RefreshRequest>().Produces(204);
        group.MapGet("/me", Me).RequireAuthorization(AuthPolicies.Authenticated).Produces<UserDto>();

        return group;
    }

    internal static UserDto ToDto(User u) => new(u.Id, u.Email, u.FullName, u.Role, u.CreatedAt);

    private static async Task<IResult> Register(RegisterRequest request, IdentityDbContext db, IPasswordHasher hasher, ITokenService tokens, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            throw new ConflictException("An account with that email already exists.");
        }

        var user = new User(email, request.FullName, hasher.Hash(request.Password), Roles.Customer);
        db.Users.Add(user);
        var response = IssueAndStore(db, tokens, user);
        await db.SaveChangesAsync(ct);

        return Results.Created("/api/auth/me", response);
    }

    private static async Task<IResult> Login(LoginRequest request, IdentityDbContext db, IPasswordHasher hasher, ITokenService tokens, ILoggerFactory loggers, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);

        if (user is null || !hasher.Verify(request.Password, user.PasswordHash))
        {
            loggers.CreateLogger("Auth").LogInformation("Failed login for {Email}", email);
            return Results.Problem(statusCode: 401, title: "Invalid credentials", detail: "Email or password is incorrect.");
        }

        user.RecordLogin();
        var response = IssueAndStore(db, tokens, user);
        await db.SaveChangesAsync(ct);
        return Results.Ok(response);
    }

    private static async Task<IResult> Refresh(RefreshRequest request, IdentityDbContext db, ITokenService tokens, CancellationToken ct)
    {
        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var existing = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        var now = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            return Results.Problem(statusCode: 401, title: "Invalid refresh token");
        }

        if (!existing.IsActive(now))
        {
            // reused revoked token = probably stolen, revoke everything
            if (existing.RevokedAt is not null)
            {
                var active = await db.RefreshTokens.Where(t => t.UserId == existing.UserId && t.RevokedAt == null).ToListAsync(ct);
                active.ForEach(t => t.Revoke());
                await db.SaveChangesAsync(ct);
            }

            return Results.Problem(statusCode: 401, title: "Refresh token expired or revoked");
        }

        var user = await db.Users.SingleAsync(u => u.Id == existing.UserId, ct);
        var response = IssueAndStore(db, tokens, user);
        existing.Revoke(tokens.HashRefreshToken(response.RefreshToken));
        await db.SaveChangesAsync(ct);
        return Results.Ok(response);
    }

    private static async Task<IResult> Logout(RefreshRequest request, IdentityDbContext db, ITokenService tokens, CancellationToken ct)
    {
        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var existing = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing is not null)
        {
            existing.Revoke();
            await db.SaveChangesAsync(ct);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> Me(ClaimsPrincipal principal, IdentityDbContext db, CancellationToken ct)
    {
        var id = principal.GetUserId();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct)
                   ?? throw new NotFoundException("User no longer exists.");
        return Results.Ok(ToDto(user));
    }

    private static AuthResponse IssueAndStore(IdentityDbContext db, ITokenService tokens, User user)
    {
        var issued = tokens.Issue(user);
        db.RefreshTokens.Add(new RefreshToken(user.Id, tokens.HashRefreshToken(issued.RefreshToken), issued.RefreshTokenExpiresAt));
        return new AuthResponse(issued.AccessToken, issued.AccessTokenExpiresAt, issued.RefreshToken, issued.RefreshTokenExpiresAt, ToDto(user));
    }
}
