using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;

namespace Notifications.Domain.Aggregates.DeviceToken;

/// <summary>
/// FCM (or APNs) device-token registration for a user. Lets the backend send push
/// notifications to specific devices when the app is closed/backgrounded — SignalR
/// handles the foreground case while the app holds an open hub connection.
///
/// One row per (UserId, Token) — re-registering the same token from the same device
/// just bumps <see cref="LastSeenAt"/> and clears any stale <see cref="RevokedAt"/>.
/// Logout revokes the row instead of deleting so we keep a small audit trail of which
/// devices a user has signed in from.
/// </summary>
public sealed class DeviceToken : AggregateRoot<DeviceTokenId>
{
    private DeviceToken() { }

    public Guid UserId { get; private set; }
    public string Token { get; private set; } = null!;
    public DevicePlatform Platform { get; private set; }
    public DateTime RegisteredAt { get; private set; }
    public DateTime LastSeenAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    public static Result<DeviceToken> Register(
        Guid userId,
        string token,
        DevicePlatform platform,
        DateTime utcNow)
    {
        if (userId == Guid.Empty)
            return Error.Validation("User id is required.");
        if (string.IsNullOrWhiteSpace(token))
            return Error.Validation("Device token is required.");
        if (token.Length > 4096)
            return Error.Validation("Device token is too long.");

        return new DeviceToken
        {
            Id = DeviceTokenId.New(),
            UserId = userId,
            Token = token.Trim(),
            Platform = platform,
            RegisteredAt = utcNow,
            LastSeenAt = utcNow,
            RevokedAt = null,
        };
    }

    /// <summary>
    /// Re-registers an existing token: bumps LastSeenAt and clears any RevokedAt so the
    /// row participates in fan-out again. Idempotent.
    /// </summary>
    public Result Refresh(DateTime utcNow)
    {
        LastSeenAt = utcNow;
        RevokedAt = null;
        return Result.Success();
    }

    /// <summary>
    /// Marks the token as revoked. Subsequent FCM sends should skip this row.
    /// Used on logout, on FCM "Unregistered" responses, and on token rotation.
    /// </summary>
    public Result Revoke(DateTime utcNow)
    {
        if (RevokedAt is not null)
            return Result.Success();

        RevokedAt = utcNow;
        return Result.Success();
    }
}
