namespace Identity.Application.Contracts;

/// <summary>Tokens returned by login/refresh. The refresh token is also set as an httpOnly cookie by the API.</summary>
public sealed record AuthTokensDto(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc);

public sealed record AuthenticatedUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    Guid RoleId,
    string RoleName,
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
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? LastLoginAtUtc);

/// <summary>Returned by InviteUser. The token is surfaced for dev/testing; production delivers it by email only.</summary>
public sealed record InvitedUserDto(Guid Id, string Email, Guid InvitationToken);

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
