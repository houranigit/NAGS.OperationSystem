using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Auth;

public enum AuthStatus
{
    Loading,
    Authenticated,
    Anonymous
}

public sealed class AuthSession
{
    private readonly BrowserApiClient apiClient;
    private readonly AuthTokenStore tokenStore;

    public AuthSession(BrowserApiClient apiClient, AuthTokenStore tokenStore, ClientTokenRefresher refresher)
    {
        this.apiClient = apiClient;
        this.tokenStore = tokenStore;
        // When a transparent refresh fails, the session is no longer valid: drop to anonymous so the
        // UI redirects to login.
        refresher.RefreshFailed += OnRefreshFailed;
    }

    public AuthStatus Status { get; private set; } = AuthStatus.Loading;

    public AuthenticatedUser? User { get; private set; }

    public event Action? StateChanged;

    public bool HasPermission(string permission) => User?.Permissions.Contains(permission) ?? false;

    private void OnRefreshFailed()
    {
        if (Status == AuthStatus.Anonymous)
            return;

        User = null;
        Status = AuthStatus.Anonymous;
        StateChanged?.Invoke();
    }

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
