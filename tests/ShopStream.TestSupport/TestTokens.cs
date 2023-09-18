using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ShopStream.Contracts;

namespace ShopStream.TestSupport;

public static class TestTokens
{
    public const string SigningKey = "integration-test-signing-key-0123456789abcdef";

    // same as SEED_DEMO_PASSWORD in .env.example
    public const string DemoPassword = "Demo@12345";

    public static string Create(Guid userId, string email, string role = Roles.Customer, TimeSpan? lifetime = null)
    {
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "shop-stream-identity",
            audience: "shop-stream",
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim("role", role)
            },
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(10)),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static HttpClient WithToken(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
