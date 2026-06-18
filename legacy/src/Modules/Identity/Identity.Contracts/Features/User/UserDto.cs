namespace Identity.Contracts.Features.User;

/// <summary>Read model for the Users grid + Update dialog (mirrors EmployeeDto's surface).</summary>
/// <param name="InvitationToken">Plain token when pending and not expired — for manual delivery if email fails.</param>
public sealed record UserDto(
    Guid Id,
    string Username,
    string Email,
    string UserType,
    string Status,
    bool IsActive,
    bool IsLocked,
    DateTime? LockedUntil,
    DateTime CreatedAt,
    DateTime? LastPasswordChangedAt,
    DateTime? PasswordExpiresAt,
    int FailedLoginAttempts,
    bool HasPendingInvitation,
    DateTime? InvitationExpiresAt,
    string? InvitationToken,
    Guid? ExternalReferenceId,
    IReadOnlyList<UserRoleSnapshot> Roles);

public sealed record UserRoleSnapshot(Guid RoleId, string Name);
