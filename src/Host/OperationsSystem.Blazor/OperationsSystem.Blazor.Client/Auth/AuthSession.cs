using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Auth;

public enum AuthStatus
{
    Loading,
    Authenticated,
    Anonymous
}

public sealed class AuthSession(BrowserApiClient apiClient, AuthTokenStore tokenStore)
{
    public AuthStatus Status { get; private set; } = AuthStatus.Loading;

    public AuthenticatedUser? User { get; private set; }

    public event Action? StateChanged;

    public bool HasPermission(string permission) => User?.Permissions.Contains(permission) ?? false;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Status is AuthStatus.Authenticated)
            return;

        try
        {
            var token = await apiClient.PostAsync<object, AccessTokenResponse>(
                "/identity/auth/refresh",
                new { },
                cancellationToken);

            tokenStore.SetAccessToken(token.AccessToken);
            User = await apiClient.GetAsync<AuthenticatedUser>("/identity/me", cancellationToken);
            Status = AuthStatus.Authenticated;
        }
        catch (ApiException)
        {
            tokenStore.SetAccessToken(null);
            User = null;
            Status = AuthStatus.Anonymous;
        }

        StateChanged?.Invoke();
    }

    public async Task LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var token = await apiClient.PostAsync<LoginRequest, AccessTokenResponse>(
            "/identity/auth/login",
            new LoginRequest(email, password),
            cancellationToken);

        tokenStore.SetAccessToken(token.AccessToken);
        User = await apiClient.GetAsync<AuthenticatedUser>("/identity/me", cancellationToken);
        Status = AuthStatus.Authenticated;
        StateChanged?.Invoke();
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await apiClient.PostAsync("/identity/auth/logout", cancellationToken);
        }
        catch (ApiException)
        {
            // Local sign-out should still complete if the server session is already gone.
        }

        tokenStore.SetAccessToken(null);
        User = null;
        Status = AuthStatus.Anonymous;
        StateChanged?.Invoke();
    }
}
