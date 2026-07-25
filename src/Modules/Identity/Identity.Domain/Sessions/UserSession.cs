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
    /// <summary>
    /// Stable lineage shared by refresh-token rotations from the same sign-in. Revoking any
    /// historical session id can therefore revoke the current successor without affecting the
    /// user's other devices.
    /// </summary>
    public Guid FamilyId { get; private set; }
    /// <summary>
    /// The user's authorization generation when this session was issued. A credential or access
    /// change rotates the user's stamp, making every session from the previous generation unusable
    /// even when refresh and revocation race each other.
    /// </summary>
    public Guid SecurityStamp { get; private set; }
    public string RefreshTokenHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public string? CreatedByIp { get; private set; }
    public string? UserAgent { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Result<UserSession> Issue(
        Guid userId,
        Guid securityStamp,
        string refreshTokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset now,
        string? createdByIp = null,
        string? userAgent = null)
    {
        if (userId == Guid.Empty)
            return Error.Validation("User is required.", "Identity.Session.UserRequired");

        if (securityStamp == Guid.Empty)
            return Error.Validation("A security stamp is required.", "Identity.Session.SecurityStampRequired");

        if (string.IsNullOrWhiteSpace(refreshTokenHash))
            return Error.Validation("Refresh token hash is required.", "Identity.Session.TokenRequired");

        if (expiresAtUtc <= now)
            return Error.Validation("Session expiry must be in the future.", "Identity.Session.InvalidExpiry");

        var sessionId = Guid.NewGuid();
        return new UserSession
        {
            Id = sessionId,
            UserId = userId,
            FamilyId = sessionId,
            SecurityStamp = securityStamp,
            RefreshTokenHash = refreshTokenHash,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = now,
            CreatedByIp = createdByIp,
            UserAgent = userAgent
        };
    }

    /// <summary>Issues the next single-use refresh token in this sign-in lineage.</summary>
    public Result<UserSession> ContinueWith(
        string refreshTokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset now,
        string? createdByIp = null,
        string? userAgent = null)
    {
        if (FamilyId == Guid.Empty)
            return Error.Conflict("The session lineage is invalid.", "Identity.Session.FamilyRequired");

        var next = Issue(
            UserId,
            SecurityStamp,
            refreshTokenHash,
            expiresAtUtc,
            now,
            createdByIp,
            userAgent);
        if (next.IsFailure)
            return next.Error;

        next.Value.FamilyId = FamilyId;
        return next.Value;
    }

    /// <summary>
    /// Keeps one browser session usable after a self-service security-stamp change. Every other
    /// session remains bound to the prior generation and therefore fails closed.
    /// </summary>
    public Result RebindSecurityStamp(Guid securityStamp)
    {
        if (securityStamp == Guid.Empty)
            return Error.Validation("A security stamp is required.", "Identity.Session.SecurityStampRequired");

        SecurityStamp = securityStamp;
        return Result.Success();
    }

    public bool IsActive(DateTimeOffset now) => RevokedAtUtc is null && ExpiresAtUtc > now;

    public Result Revoke(DateTimeOffset now)
    {
        if (RevokedAtUtc is not null)
            return Result.Success();

        // A caller may have captured its clock before waiting on this family's lock. Never persist
        // an impossible chronology when the session was created by the operation that won the race.
        RevokedAtUtc = now < CreatedAtUtc ? CreatedAtUtc : now;
        return Result.Success();
    }
}
