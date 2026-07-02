namespace OperationsSystem.Blazor.Client.Api;

/// <summary>Standard list envelope returned by the API. Mirrors the backend <c>PagedResult&lt;T&gt;</c>.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

// --- Roles -----------------------------------------------------------------

/// <summary>The fixed account types. Mirrors the backend <c>UserType</c>; serialized as its name.</summary>
public static class UserTypes
{
    public const string SystemAdministrator = nameof(SystemAdministrator);
    public const string StationStaff = nameof(StationStaff);
    public const string CustomerContact = nameof(CustomerContact);

    public static readonly IReadOnlyList<string> All = [SystemAdministrator, StationStaff, CustomerContact];
}

public sealed record RoleListItem(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    string CompatibleUserType,
    int PermissionCount,
    int UserCount);

public sealed record RoleDetail(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    string CompatibleUserType,
    IReadOnlyList<string> Permissions,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record PermissionGroup(string Resource, IReadOnlyList<string> Permissions);

public sealed record CreateRoleRequest(string Name, string? Description, string CompatibleUserType, IReadOnlyList<string> Permissions);
public sealed record UpdateRoleRequest(string Name, string? Description);
public sealed record UpdateRolePermissionsRequest(IReadOnlyList<string> Permissions);

// --- Users -----------------------------------------------------------------

public sealed record UserListItem(
    Guid Id,
    string Email,
    string DisplayName,
    string Status,
    bool IsLockedOut,
    Guid RoleId,
    string RoleName,
    string UserType,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc);

public sealed record UserDetail(
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

public sealed record InvitedUser(Guid Id, string Email, string DeliveryStatus);

public sealed record RoleOption(Guid Id, string Name, string CompatibleUserType);

public sealed record InviteUserRequest(string Email, string DisplayName, Guid? RoleId = null);
public sealed record UpdateUserRequest(string DisplayName);
public sealed record AssignRoleRequest(Guid RoleId);

// --- Sessions --------------------------------------------------------------

public sealed record UserSession(
    Guid Id,
    Guid UserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc,
    bool IsActive,
    bool IsCurrent,
    string? CreatedByIp,
    string? UserAgent);

// --- Auth ------------------------------------------------------------------

public sealed record ActivateAccountRequest(string Email, string InvitationToken, string NewPassword);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record ConfirmEmailChangeRequest(string Token, string NewEmail);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string NewPassword);
public sealed record MfaEnrollment(string Secret, string OtpAuthUri);
public sealed record MfaRecoveryCodes(IReadOnlyList<string> RecoveryCodes);
public sealed record ConfirmMfaRequest(string Code);
