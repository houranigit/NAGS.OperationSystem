namespace Identity.Application.Contracts;

/// <summary>Tokens returned by login/refresh. The refresh token is also set as an httpOnly cookie by the API.</summary>
public sealed record AuthTokensDto(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc);

/// <summary>
/// Outcome of the first login step. When <see cref="MfaRequired"/> is true the caller must complete
/// the second step with <see cref="MfaToken"/>; otherwise <see cref="Tokens"/> is populated.
/// </summary>
public sealed record LoginResultDto(bool MfaRequired, string? MfaToken, AuthTokensDto? Tokens);

/// <summary>Returned by MFA enrollment: the shared secret and the otpauth URI for an authenticator app.</summary>
public sealed record MfaEnrollmentDto(string Secret, string OtpAuthUri);

/// <summary>One-time recovery codes shown once, after MFA is confirmed.</summary>
public sealed record MfaRecoveryCodesDto(IReadOnlyList<string> RecoveryCodes);

public sealed record AuthenticatedUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    Guid RoleId,
    string RoleName,
    string UserType,
    Guid? ExternalReferenceId,
    string PortalSource,
    bool MfaEnabled,
    bool MfaEnrollmentRequired,
    IReadOnlyList<string> Permissions);

public sealed record RoleListItemDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    string CompatibleUserType,
    int PermissionCount,
    int UserCount);

public sealed record RoleDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    string CompatibleUserType,
    IReadOnlyList<string> Permissions,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

/// <summary>A role option for pickers, with its compatible user type so the UI can filter by account type.</summary>
public sealed record RoleOptionDto(Guid Id, string Name, string CompatibleUserType);

public sealed record PermissionGroupDto(string Resource, IReadOnlyList<string> Permissions);

public sealed record UserListItemDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Status,
    bool IsLockedOut,
    Guid RoleId,
    string RoleName,
    string UserType,
    Guid? ExternalReferenceId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc);

public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Status,
    bool IsLockedOut,
    DateTimeOffset? LockoutEndUtc,
    Guid RoleId,
    string RoleName,
    string UserType,
    Guid? ExternalReferenceId,
    string PortalSource,
    bool MfaEnabled,
    bool MfaEnrollmentRequired,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? LastLoginAtUtc);

/// <summary>
/// Returned by InviteUser. The invitation token is never exposed; it is delivered only by email.
/// <see cref="DeliveryStatus"/> reports the queued/sent state of that delivery.
/// </summary>
public sealed record InvitedUserDto(Guid Id, string Email, string DeliveryStatus);

/// <summary>
/// A refresh-token session. <see cref="IsCurrent"/> marks the session backing the caller's
/// current refresh-token cookie (only meaningful for self-service "my sessions" queries).
/// </summary>
public sealed record UserSessionDto(
    Guid Id,
    Guid UserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc,
    bool IsActive,
    bool IsCurrent,
    string? CreatedByIp,
    string? UserAgent);
