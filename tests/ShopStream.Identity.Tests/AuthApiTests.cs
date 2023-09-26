using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ShopStream.Contracts;
using ShopStream.Identity.Data;
using ShopStream.Identity.Endpoints;
using ShopStream.TestSupport;

namespace ShopStream.Identity.Tests;

public sealed class IdentityApiFactory : ServiceFactory<Program, IdentityDbContext> { }

public class AuthApiTests : IClassFixture<IdentityApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IdentityApiFactory _factory;
    private readonly HttpClient _client;

    public AuthApiTests(IdentityApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task Register_issues_tokens_for_a_customer()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(NewEmail(), "Passw0rd!", "New Person"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        auth.User.Role.Should().Be("Customer");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(auth.AccessToken);
        jwt.Claims.Should().Contain(c => c.Type == "role" && c.Value == "Customer");
        jwt.Issuer.Should().Be("shop-stream-identity");
    }

    [Fact]
    public async Task Duplicate_email_conflicts_case_insensitively()
    {
        var email = NewEmail();
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "Passw0rd!", "One"));
        var again = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email.ToUpperInvariant(), "Passw0rd!", "Two"));
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Weak_passwords_are_rejected()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("not-an-email", "short", ""));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors").EnumerateObject().Select(e => e.Name).Should().BeEquivalentTo(new[] { "Email", "Password", "FullName" });
    }

    [Fact]
    public async Task Seeded_admin_can_log_in_and_wrong_password_cannot()
    {
        var ok = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(DemoAccounts.AdminEmail, TestTokens.DemoPassword));
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadFromJsonAsync<AuthResponse>(Json))!.User.Role.Should().Be("Admin");

        var bad = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(DemoAccounts.AdminEmail, "nope"));
        bad.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_rotates_and_detects_reuse()
    {
        var registered = (await (await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(NewEmail(), "Passw0rd!", "Rotator")))
            .Content.ReadFromJsonAsync<AuthResponse>(Json))!;

        var first = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(registered.RefreshToken));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = (await first.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        rotated.RefreshToken.Should().NotBe(registered.RefreshToken);

        // replaying the old token revokes the whole family
        (await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(registered.RefreshToken))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(rotated.RefreshToken))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var auth = (await (await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(DemoAccounts.CustomerEmail, TestTokens.DemoPassword)))
            .Content.ReadFromJsonAsync<AuthResponse>(Json))!;

        (await _client.PostAsJsonAsync("/api/auth/logout", new RefreshRequest(auth.RefreshToken))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(auth.RefreshToken))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_returns_the_current_user()
    {
        var auth = (await (await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(DemoAccounts.CustomerEmail, TestTokens.DemoPassword)))
            .Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        var me = await _factory.CreateClient().WithToken(auth.AccessToken).GetFromJsonAsync<UserDto>("/api/auth/me", Json);
        me!.Email.Should().Be(DemoAccounts.CustomerEmail);

        (await _client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Demo_accounts_use_fixed_ids_so_other_seeders_can_reference_them()
    {
        var jane = await (await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(DemoAccounts.JaneEmail, TestTokens.DemoPassword)))
            .Content.ReadFromJsonAsync<AuthResponse>(Json);
        jane!.User.Id.Should().Be(DemoAccounts.JaneId);
        jane.User.Role.Should().Be("Customer");
    }

    [Fact]
    public async Task User_admin_is_restricted_to_admins()
    {
        var customer = await (await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(DemoAccounts.CustomerEmail, TestTokens.DemoPassword))).Content.ReadFromJsonAsync<AuthResponse>(Json);
        (await _factory.CreateClient().WithToken(customer!.AccessToken).GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var admin = await (await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(DemoAccounts.AdminEmail, TestTokens.DemoPassword))).Content.ReadFromJsonAsync<AuthResponse>(Json);
        var users = await _factory.CreateClient().WithToken(admin!.AccessToken).GetFromJsonAsync<JsonElement>("/api/users?search=shopstream.local");
        users.GetProperty("total").GetInt32().Should().BeGreaterOrEqualTo(3);
    }
}
