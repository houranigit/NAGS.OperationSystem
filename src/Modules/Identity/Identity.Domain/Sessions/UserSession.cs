using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;

namespace Identity.Domain.Sessions;

/// <summary>
/// A refresh-token session. The raw refresh token is never stored; only a hash is kept so a
/// leaked database cannot be used to mint access tokens. Sessions are rotated on refresh and
/// can be revoked individually or all at once.
/// </summary>
public sealed class UserSession : AggregateRoot<Guid>
{
    private UserSession() { }

    public Guid UserId { get; private set; }
    public string RefreshTokenHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public string? CreatedByIp { get; private set; }
    public string? UserAgent { get; private set; }

    public static Result<UserSession> Issue(
        Guid userId,
        string refreshTokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset now,
        string? createdByIp = null,
        string? userAgent = null)
    {
        if (userId == Guid.Empty)
            return Error.Validation("User is required.", "Identity.Session.UserRequired");

        if (string.IsNullOrWhiteSpace(refreshTokenHash))
            return Error.Validation("Refresh token hash is required.", "Identity.Session.TokenRequired");

        if (expiresAtUtc <= now)
            return Error.Validation("Session expiry must be in the future.", "Identity.Session.InvalidExpiry");

        return new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RefreshTokenHash = refreshTokenHash,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = now,
            CreatedByIp = createdByIp,
            UserAgent = userAgent
        };
    }

    public bool IsActive(DateTimeOffset now) => RevokedAtUtc is null && ExpiresAtUtc > now;

    public Result Revoke(DateTimeOffset now)
    {
        if (RevokedAtUtc is not null)
            return Result.Success();

        RevokedAtUtc = now;
        return Result.Success();
    }
}
