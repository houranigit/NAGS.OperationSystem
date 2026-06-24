namespace OperationsSystem.Blazor.Client.Auth;

public sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);

public sealed record AuthenticatedUser(
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

public sealed record LoginRequest(string Email, string Password);
