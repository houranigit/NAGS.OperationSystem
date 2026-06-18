using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Identity.Domain.Aggregates.User;
using Identity.Domain.Events;

namespace Identity.Domain.Aggregates.UserSession;

public sealed class UserSession : AggregateRoot<UserSessionId>
{
    public UserId UserId { get; private set; } = null!;
    public string AccessToken { get; private set; } = null!;
    public string RefreshToken { get; private set; } = null!;
    public string? DeviceInfo { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime AccessTokenExpiresAt { get; private set; }
    public DateTime RefreshTokenExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }

    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsExpired => DateTime.UtcNow > RefreshTokenExpiresAt;
    public bool IsActive => !IsRevoked && !IsExpired;

    private UserSession() { }

    public static Result<UserSession> Create(
        UserId userId,
        string accessToken,
        string refreshToken,
        DateTime accessTokenExpiresAt,
        DateTime refreshTokenExpiresAt,
        string? deviceInfo = null,
        string? ipAddress = null,
        string? userAgent = null)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return Error.Validation("Access token is required.");
        if (string.IsNullOrWhiteSpace(refreshToken))
            return Error.Validation("Refresh token is required.");

        var session = new UserSession
        {
            Id = UserSessionId.New(),
            UserId = userId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAt = accessTokenExpiresAt,
            RefreshTokenExpiresAt = refreshTokenExpiresAt,
            DeviceInfo = deviceInfo,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CreatedAt = DateTime.UtcNow
        };

        session.RaiseDomainEvent(new UserSessionCreatedEvent(session.Id, userId));
        return session;
    }

    public Result Revoke(string reason)
    {
        if (IsRevoked)
            return Error.Conflict("Session is already revoked.");

        RevokedAt = DateTime.UtcNow;
        RevokedReason = reason;
        RaiseDomainEvent(new UserSessionRevokedEvent(Id, UserId));
        return Result.Success();
    }

    public Result Refresh(
        string newAccessToken,
        string newRefreshToken,
        DateTime newAccessTokenExpiresAt,
        DateTime newRefreshTokenExpiresAt)
    {
        if (!IsActive)
            return Error.Conflict("Cannot refresh an inactive session.");
        if (string.IsNullOrWhiteSpace(newAccessToken))
            return Error.Validation("New access token is required.");
        if (string.IsNullOrWhiteSpace(newRefreshToken))
            return Error.Validation("New refresh token is required.");

        AccessToken = newAccessToken;
        RefreshToken = newRefreshToken;
        AccessTokenExpiresAt = newAccessTokenExpiresAt;
        RefreshTokenExpiresAt = newRefreshTokenExpiresAt;
        return Result.Success();
    }
}
