using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Infrastructure.Security;

/// <summary>Claim types issued in Identity access tokens.</summary>
public static class IdentityClaimTypes
{
    public const string Permission = "permission";

    /// <summary>The user's security stamp; a mismatch invalidates the token after a credential/role change.</summary>
    public const string SecurityStamp = "security_stamp";

    /// <summary>The id of the refresh session backing this access token; revoking it kills the token.</summary>
    public const string SessionId = "sid";
}

public sealed class TokenService(TimeProvider timeProvider, IOptions<IdentityModuleOptions> options) : ITokenService
{
    private readonly IdentityModuleOptions _options = options.Value;

    public AccessToken CreateAccessToken(User user, IReadOnlyCollection<string> permissions, Guid sessionId)
    {
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email.Value),
            new(JwtRegisteredClaimNames.Name, user.DisplayName),
            new("roleId", user.RoleId.ToString()),
            new(AuthorizationClaimTypes.UserType, user.UserType.ToString()),
            new(IdentityClaimTypes.SecurityStamp, user.SecurityStamp.ToString()),
            new(IdentityClaimTypes.SessionId, sessionId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (user.ExternalReferenceId is { } externalRef)
            claims.Add(new Claim(AuthorizationClaimTypes.ExternalReference, externalRef.ToString()));

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

    public string HashRefreshToken(string rawToken) => HashToken(rawToken);

    public SecureToken CreateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var raw = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return new SecureToken(raw, HashToken(raw));
    }

    public string HashToken(string rawToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash);
    }

    private const string MfaPurpose = "mfa-challenge";

    public string CreateMfaChallengeToken(Guid userId)
    {
        var now = timeProvider.GetUtcNow();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("purpose", MfaPurpose),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Jwt.SigningKey));
        var token = new JwtSecurityToken(
            issuer: _options.Jwt.Issuer,
            audience: _options.Jwt.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.AddMinutes(5).UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public Guid? ValidateMfaChallengeToken(string token)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Jwt.SigningKey));
        try
        {
            // Keep raw claim names ("sub", "purpose") rather than the default long-URI remapping.
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _options.Jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = _options.Jwt.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            }, out _);

            if (principal.FindFirstValue("purpose") != MfaPurpose)
                return null;

            return Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;
        }
        catch
        {
            return null;
        }
    }
}
