using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace OperationsSystem.Blazor.Client.Auth;

/// <summary>
/// Bridges <see cref="AuthSession"/> (the in-memory access token + refresh-cookie flow) to Blazor's
/// authentication/authorization system so <c>AuthorizeView</c>, <c>[Authorize]</c>, and permission
/// checks work. Permissions are exposed as <c>permission</c> claims.
/// </summary>
public sealed class PortalAuthStateProvider : AuthenticationStateProvider, IDisposable
{
    public const string PermissionClaimType = "permission";
    public const string RoleNameClaimType = "role_name";

    private readonly AuthSession _auth;

    public PortalAuthStateProvider(AuthSession auth)
    {
        _auth = auth;
        _auth.StateChanged += OnAuthStateChanged;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        await _auth.InitializeAsync();
        return new AuthenticationState(BuildPrincipal(_auth.User));
    }

    private void OnAuthStateChanged() =>
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(BuildPrincipal(_auth.User))));

    private static ClaimsPrincipal BuildPrincipal(AuthenticatedUser? user)
    {
        if (user is null)
            return new ClaimsPrincipal(new ClaimsIdentity());

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email),
            new(RoleNameClaimType, user.RoleName)
        };
        claims.AddRange(user.Permissions.Select(p => new Claim(PermissionClaimType, p)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Portal"));
    }

    public void Dispose() => _auth.StateChanged -= OnAuthStateChanged;
}
