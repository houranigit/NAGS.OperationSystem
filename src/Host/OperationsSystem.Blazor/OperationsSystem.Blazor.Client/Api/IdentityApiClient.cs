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

    public Task ConfirmEmailChangeAsync(ConfirmEmailChangeRequest request, CancellationToken ct = default) =>
        api.PostAsync("/identity/auth/confirm-email-change", request, ct);

    public Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default) =>
        api.PostAsync("/identity/auth/forgot-password", request, ct);

    public Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default) =>
        api.PostAsync("/identity/auth/reset-password", request, ct);

    public Task<MfaEnrollment> EnrollMfaAsync(CancellationToken ct = default) =>
        api.PostAsync<object, MfaEnrollment>("/identity/auth/mfa/enroll", new { }, ct);

    public Task<MfaRecoveryCodes> ConfirmMfaAsync(ConfirmMfaRequest request, CancellationToken ct = default) =>
        api.PostAsync<ConfirmMfaRequest, MfaRecoveryCodes>("/identity/auth/mfa/confirm", request, ct);

    // --- Users -------------------------------------------------------------

    public Task<PagedResult<UserListItem>> GetUsersAsync(
        int page,
        int pageSize,
        string? search,
        string? status,
        Guid? roleId = null,
        string? userType = null,
        string? sort = null,
        CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("page", page)
            .Add("pageSize", pageSize)
            .Add("search", search)
            .Add("status", status)
            .Add("roleId", roleId?.ToString())
            .Add("userType", userType)
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

    public Task SuspendUserAsync(Guid id, CancellationToken ct = default) =>
        api.PostAsync($"/identity/users/{id}/suspend", ct);

    public Task RestoreAccessAsync(Guid id, CancellationToken ct = default) =>
        api.PostAsync($"/identity/users/{id}/restore-access", ct);

    public Task ResetMfaAsync(Guid id, CancellationToken ct = default) =>
        api.PostAsync($"/identity/users/{id}/mfa/reset", ct);

    public Task DeactivateUserAsync(Guid id, CancellationToken ct = default) =>
        api.PostAsync($"/identity/users/{id}/deactivate", ct);

    public Task ResendInvitationAsync(Guid id, CancellationToken ct = default) =>
        api.PostAsync($"/identity/users/{id}/resend-invitation", ct);

    // --- Roles -------------------------------------------------------------

    public Task<PagedResult<RoleListItem>> GetRolesAsync(
        int page,
        int pageSize,
        string? search,
        string? userType = null,
        string? sort = null,
        CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("page", page)
            .Add("pageSize", pageSize)
            .Add("search", search)
            .Add("userType", userType)
            .Add("sort", sort)
            .Build();
        return api.GetAsync<PagedResult<RoleListItem>>($"/identity/roles{query}", ct);
    }

    public Task<RoleDetail> GetRoleAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<RoleDetail>($"/identity/roles/{id}", ct);

    /// <summary>Role options, optionally compatible with a given user type, for filters and assignment pickers.</summary>
    public Task<IReadOnlyList<RoleOption>> GetRoleOptionsAsync(string? userType = null, CancellationToken ct = default)
    {
        var query = string.IsNullOrWhiteSpace(userType) ? string.Empty : $"?userType={Uri.EscapeDataString(userType)}";
        return api.GetAsync<IReadOnlyList<RoleOption>>($"/identity/roles/options{query}", ct);
    }

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

    public Task<PagedResult<UserSession>> GetUserSessionsAsync(
        Guid userId,
        int page = 1,
        int pageSize = 20,
        bool activeOnly = false,
        CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("page", page)
            .Add("pageSize", pageSize)
            .Add("activeOnly", activeOnly.ToString().ToLowerInvariant())
            .Build();
        return api.GetAsync<PagedResult<UserSession>>($"/identity/users/{userId}/sessions{query}", ct);
    }

    public Task RevokeUserSessionsAsync(Guid userId, CancellationToken ct = default) =>
        api.PostAsync($"/identity/users/{userId}/sessions/revoke-all", ct);

    public Task RevokeSessionAsync(Guid sessionId, CancellationToken ct = default) =>
        api.DeleteAsync($"/identity/sessions/{sessionId}", ct);

    public Task<PagedResult<UserSession>> GetMySessionsAsync(int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("page", page)
            .Add("pageSize", pageSize)
            .Build();
        return api.GetAsync<PagedResult<UserSession>>($"/identity/me/sessions{query}", ct);
    }

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
