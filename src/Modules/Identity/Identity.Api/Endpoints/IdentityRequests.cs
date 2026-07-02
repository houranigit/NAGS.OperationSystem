namespace Identity.Api.Endpoints;

// Auth
public sealed record LoginRequest(string Email, string Password);
public sealed record ActivateAccountRequest(string Email, string InvitationToken, string NewPassword);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record ConfirmEmailChangeRequest(string Token, string NewEmail);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string NewPassword);
public sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);
public sealed record LoginChallengeResponse(bool MfaRequired, string MfaToken);
public sealed record LoginMfaRequest(string MfaToken, string Code);
public sealed record ConfirmMfaRequest(string Code);

// Roles
public sealed record CreateRoleRequest(string Name, string? Description, BuildingBlocks.Contracts.Authorization.UserType? CompatibleUserType, IReadOnlyList<string> Permissions);
public sealed record UpdateRoleRequest(string Name, string? Description);
public sealed record UpdateRolePermissionsRequest(IReadOnlyList<string> Permissions);

// Users
public sealed record InviteUserRequest(string Email, string DisplayName, Guid? RoleId = null);
public sealed record UpdateUserRequest(string DisplayName);
public sealed record AssignRoleRequest(Guid RoleId);
