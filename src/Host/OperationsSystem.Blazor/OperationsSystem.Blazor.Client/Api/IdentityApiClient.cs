using System.Globalization;

namespace OperationsSystem.Blazor.Client.Api;

/// <summary>
/// Typed access to the Identity module API (<c>/api/v1/identity</c>). Components use this instead of
/// hand-written request shapes. Auth/login lives in <see cref="Auth.AuthSession"/>.
/// </summary>
public sealed class IdentityApiClient(BrowserApiClient api)
{
    // --- Account / auth (self) --------------------------------------------

    public Task ActivateAsync(ActivateAccountRequest request, CancellationToken ct = default) =>
        api.PostAsync("/identity/auth/activate", request, ct);

    public Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default) =>
        api.PostAsync("/identity/auth/change-password", request, ct);

    // --- Users -------------------------------------------------------------

    public Task<PagedResult<UserListItem>> GetUsersAsync(
        int page,
        int pageSize,
        string? search,
        string? status,
        Guid? roleId = null,
        string? sort = null,
        CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("page", page)
            .Add("pageSize", pageSize)
            .Add("search", search)
            .Add("status", status)
            .Add("roleId", roleId?.ToString())
            .Add("sort", sort)
            .Build();
        return api.GetAsync<PagedResult<UserListItem>>($"/identity/users{query}", ct);
    }

    public Task<UserDetail> GetUserAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<UserDetail>($"/identity/users/{id}", ct);

    public Task<InvitedUser> InviteUserAsync(InviteUserRequest request, CancellationToken ct = default) =>
        api.PostAsync<InviteUserRequest, InvitedUser>("/identity/users/invite", request, ct);

    public Task UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default) =>
        api.PutAsync($"/identity/users/{id}", request, ct);

    public Task AssignRoleAsync(Guid id, AssignRoleRequest request, CancellationToken ct = default) =>
        api.PutAsync($"/identity/users/{id}/role", request, ct);

    public Task LockUserAsync(Guid id, CancellationToken ct = default) =>
        api.PostAsync($"/identity/users/{id}/lock", ct);

    public Task UnlockUserAsync(Guid id, CancellationToken ct = default) =>
        api.PostAsync($"/identity/users/{id}/unlock", ct);

    public Task DeactivateUserAsync(Guid id, CancellationToken ct = default) =>
        api.PostAsync($"/identity/users/{id}/deactivate", ct);

    public Task ResendInvitationAsync(Guid id, CancellationToken ct = default) =>
        api.PostAsync($"/identity/users/{id}/resend-invitation", ct);

    // --- Roles -------------------------------------------------------------

    public Task<PagedResult<RoleListItem>> GetRolesAsync(
        int page,
        int pageSize,
        string? search,
        string? sort = null,
        CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("page", page)
            .Add("pageSize", pageSize)
            .Add("search", search)
            .Add("sort", sort)
            .Build();
        return api.GetAsync<PagedResult<RoleListItem>>($"/identity/roles{query}", ct);
    }

    public Task<RoleDetail> GetRoleAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<RoleDetail>($"/identity/roles/{id}", ct);

    /// <summary>Roles compatible with a given user type, for portal-access pickers (server-filtered).</summary>
    public Task<IReadOnlyList<RoleOption>> GetRoleOptionsAsync(string userType, CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<RoleOption>>($"/identity/roles/options?userType={Uri.EscapeDataString(userType)}", ct);

    public Task<Guid> CreateRoleAsync(CreateRoleRequest request, CancellationToken ct = default) =>
        api.PostAsync<CreateRoleRequest, Guid>("/identity/roles", request, ct);

    public Task UpdateRoleAsync(Guid id, UpdateRoleRequest request, CancellationToken ct = default) =>
        api.PutAsync($"/identity/roles/{id}", request, ct);

    public Task UpdateRolePermissionsAsync(Guid id, UpdateRolePermissionsRequest request, CancellationToken ct = default) =>
        api.PutAsync($"/identity/roles/{id}/permissions", request, ct);

    public Task DeleteRoleAsync(Guid id, CancellationToken ct = default) =>
        api.DeleteAsync($"/identity/roles/{id}", ct);

    public Task<IReadOnlyList<PermissionGroup>> GetPermissionCatalogAsync(string? userType = null, CancellationToken ct = default)
    {
        var query = string.IsNullOrWhiteSpace(userType) ? string.Empty : $"?userType={Uri.EscapeDataString(userType)}";
        return api.GetAsync<IReadOnlyList<PermissionGroup>>($"/identity/permissions{query}", ct);
    }

    // --- Sessions ----------------------------------------------------------

    public Task<IReadOnlyList<UserSession>> GetUserSessionsAsync(Guid userId, bool activeOnly = false, CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<UserSession>>($"/identity/users/{userId}/sessions?activeOnly={activeOnly.ToString().ToLowerInvariant()}", ct);

    public Task RevokeUserSessionsAsync(Guid userId, CancellationToken ct = default) =>
        api.PostAsync($"/identity/users/{userId}/sessions/revoke-all", ct);

    public Task RevokeSessionAsync(Guid sessionId, CancellationToken ct = default) =>
        api.DeleteAsync($"/identity/sessions/{sessionId}", ct);

    public Task<IReadOnlyList<UserSession>> GetMySessionsAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<UserSession>>("/identity/me/sessions", ct);

    public Task RevokeMySessionAsync(Guid sessionId, CancellationToken ct = default) =>
        api.DeleteAsync($"/identity/me/sessions/{sessionId}", ct);

    public Task RevokeMyOtherSessionsAsync(CancellationToken ct = default) =>
        api.PostAsync("/identity/me/sessions/revoke-others", ct);

    private sealed class QueryBuilder
    {
        private readonly List<string> _parts = [];

        public QueryBuilder Add(string key, int value)
        {
            _parts.Add($"{key}={value.ToString(CultureInfo.InvariantCulture)}");
            return this;
        }

        public QueryBuilder Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                _parts.Add($"{key}={Uri.EscapeDataString(value)}");
            return this;
        }

        public string Build() => _parts.Count == 0 ? string.Empty : "?" + string.Join('&', _parts);
    }
}
