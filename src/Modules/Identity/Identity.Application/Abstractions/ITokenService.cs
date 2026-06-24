using Identity.Domain.Users;

namespace Identity.Application.Abstractions;

/// <summary>Issues JWT access tokens and opaque refresh tokens.</summary>
public interface ITokenService
{
    /// <summary>Issues an access token bound to <paramref name="sessionId"/> and the user's security stamp.</summary>
    public AccessToken CreateAccessToken(User user, IReadOnlyCollection<string> permissions, Guid sessionId);

    /// <summary>Generates a new opaque refresh token and its storage hash.</summary>
    public RefreshToken CreateRefreshToken();

    /// <summary>Hashes a raw refresh token for lookup/comparison against stored sessions.</summary>
    public string HashRefreshToken(string rawToken);

    /// <summary>
    /// Generates a cryptographically random, URL-safe token (for invitations, email change,
    /// password reset) and its storage hash. Only the hash is persisted; the raw value is delivered
    /// to the recipient and never stored or returned in API responses.
    /// </summary>
    public SecureToken CreateSecureToken();

    /// <summary>Hashes a raw secure token for lookup/comparison against stored hashes.</summary>
    public string HashToken(string rawToken);

    /// <summary>Issues a short-lived token that authorizes only the second (MFA) step of sign-in.</summary>
    public string CreateMfaChallengeToken(Guid userId);

    /// <summary>Validates an MFA challenge token and returns the user id it authorizes, or null.</summary>
    public Guid? ValidateMfaChallengeToken(string token);
}

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);

public sealed record RefreshToken(string Value, string Hash, DateTimeOffset ExpiresAtUtc);

/// <summary>A raw secret token and its storage hash. Persist only <see cref="Hash"/>.</summary>
public sealed record SecureToken(string Value, string Hash);
