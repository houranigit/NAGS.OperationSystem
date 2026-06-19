using Identity.Domain.Users;

namespace Identity.Application.Abstractions;

/// <summary>Issues JWT access tokens and opaque refresh tokens.</summary>
public interface ITokenService
{
    public AccessToken CreateAccessToken(User user, IReadOnlyCollection<string> permissions);

    /// <summary>Generates a new opaque refresh token and its storage hash.</summary>
    public RefreshToken CreateRefreshToken();

    /// <summary>Hashes a raw refresh token for lookup/comparison against stored sessions.</summary>
    public string HashRefreshToken(string rawToken);
}

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);

public sealed record RefreshToken(string Value, string Hash, DateTimeOffset ExpiresAtUtc);
