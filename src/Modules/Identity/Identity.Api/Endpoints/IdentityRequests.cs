namespace Identity.Api.Endpoints;

// Auth
public sealed record LoginRequest(string Email, string Password);
public sealed record ActivateAccountRequest(string Email, Guid InvitationToken, string NewPassword);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);

// Roles
public sealed record CreateRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);
public sealed record UpdateRoleRequest(string Name, string? Description);
public sealed record UpdateRolePermissionsRequest(IReadOnlyList<string> Permissions);

// Users
public sealed record InviteUserRequest(string Email, string DisplayName, Guid RoleId);
public sealed record UpdateUserRequest(string DisplayName);
public sealed record AssignRoleRequest(Guid RoleId);
