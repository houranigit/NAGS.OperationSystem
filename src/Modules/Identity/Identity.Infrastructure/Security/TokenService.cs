using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Infrastructure.Security;

/// <summary>Claim type carrying a single granted permission. Authorized per endpoint.</summary>
public static class IdentityClaimTypes
{
    public const string Permission = "permission";
}

public sealed class TokenService(TimeProvider timeProvider, IOptions<IdentityModuleOptions> options) : ITokenService
{
    private readonly IdentityModuleOptions _options = options.Value;

    public AccessToken CreateAccessToken(User user, IReadOnlyCollection<string> permissions)
    {
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email.Value),
            new(JwtRegisteredClaimNames.Name, user.DisplayName),
            new("roleId", user.RoleId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        claims.AddRange(permissions.Select(p => new Claim(IdentityClaimTypes.Permission, p)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Jwt.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Jwt.Issuer,
            audience: _options.Jwt.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        var value = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessToken(value, expires);
    }

    public RefreshToken CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var raw = Convert.ToBase64String(bytes);
        var hash = HashRefreshToken(raw);
        var expires = timeProvider.GetUtcNow().AddDays(_options.RefreshTokenDays);
        return new RefreshToken(raw, hash, expires);
    }

    public string HashRefreshToken(string rawToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash);
    }
}
