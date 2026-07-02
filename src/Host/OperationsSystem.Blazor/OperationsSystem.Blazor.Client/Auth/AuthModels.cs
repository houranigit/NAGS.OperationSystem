namespace OperationsSystem.Blazor.Client.Auth;

public sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);

public sealed record LoginResponse(bool MfaRequired, string? MfaToken, string? AccessToken, DateTimeOffset? ExpiresAtUtc);

public sealed record LoginOutcome(bool MfaRequired, string? MfaToken)
{
    public static LoginOutcome SignedIn { get; } = new(false, null);
    public static LoginOutcome RequiresMfa(string token) => new(true, token);
}

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
public sealed record LoginMfaRequest(string MfaToken, string Code);
