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

// Mobile auth: the refresh token travels in the JSON body instead of the web httpOnly cookie,
// because native clients keep it in secure device storage (Keychain/EncryptedSharedPreferences).
public sealed record MobileTokensResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc);
public sealed record MobileRefreshRequest(string RefreshToken);
public sealed record MobileLogoutRequest(string? RefreshToken);

// Roles
public sealed record CreateRoleRequest(string Name, string? Description, BuildingBlocks.Contracts.Authorization.UserType? CompatibleUserType, IReadOnlyList<string> Permissions);
public sealed record UpdateRoleRequest(string Name, string? Description);
public sealed record UpdateRolePermissionsRequest(IReadOnlyList<string> Permissions);
public sealed record UpdateRoleAndPermissionsRequest(string Name, string? Description, IReadOnlyList<string> Permissions);

// Users
public sealed record InviteUserRequest(string Email, string DisplayName, Guid? RoleId = null);
public sealed record UpdateUserRequest(string DisplayName);
public sealed record AssignRoleRequest(Guid RoleId);
