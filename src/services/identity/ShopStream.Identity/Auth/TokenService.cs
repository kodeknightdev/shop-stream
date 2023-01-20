using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ShopStream.BuildingBlocks.Auth;
using ShopStream.Identity.Domain;

namespace ShopStream.Identity.Auth;

public sealed record IssuedTokens(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

public interface ITokenService
{
    IssuedTokens Issue(User user);
    string HashRefreshToken(string token);
}

public sealed class TokenService : ITokenService
{
    private readonly JwtSettings _settings;
    private readonly SigningCredentials _credentials;

    public TokenService(IOptions<JwtSettings> settings)
    {
        _settings = settings.Value;
        _credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SigningKey)), SecurityAlgorithms.HmacSha256);
    }

    public IssuedTokens Issue(User user)
    {
        var now = DateTimeOffset.UtcNow;
        var accessExpires = now.AddMinutes(_settings.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("role", user.Role)
        };

        var jwt = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: accessExpires.UtcDateTime,
            signingCredentials: _credentials);

        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        return new IssuedTokens(new JwtSecurityTokenHandler().WriteToken(jwt), accessExpires, refreshToken, now.AddDays(_settings.RefreshTokenDays));
    }

    public string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
