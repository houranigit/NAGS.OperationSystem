using System.Text.Json;
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

    public async Task<LoginOutcome> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var response = await apiClient.PostAsync<LoginRequest, LoginResponse>(
            "/identity/auth/login",
            new LoginRequest(email, password),
            cancellationToken);

        if (response.MfaRequired)
        {
            tokenStore.SetAccessToken(null);
            User = null;
            Status = AuthStatus.Anonymous;
            StateChanged?.Invoke();

            if (string.IsNullOrWhiteSpace(response.MfaToken))
                throw new JsonException("The MFA challenge response did not include a challenge token.");

            return LoginOutcome.RequiresMfa(response.MfaToken);
        }

        if (string.IsNullOrWhiteSpace(response.AccessToken) || response.ExpiresAtUtc is null)
            throw new JsonException("The login response did not include an access token.");

        await EstablishAuthenticatedSessionAsync(
            new AccessTokenResponse(response.AccessToken, response.ExpiresAtUtc.Value),
            cancellationToken);

        return LoginOutcome.SignedIn;
    }

    public async Task CompleteMfaLoginAsync(string mfaToken, string code, CancellationToken cancellationToken = default)
    {
        var token = await apiClient.PostAsync<LoginMfaRequest, AccessTokenResponse>(
            "/identity/auth/login/mfa",
            new LoginMfaRequest(mfaToken, code),
            cancellationToken);

        await EstablishAuthenticatedSessionAsync(token, cancellationToken);
    }

    public async Task ReloadUserAsync(CancellationToken cancellationToken = default)
    {
        if (Status is not AuthStatus.Authenticated)
            return;

        var token = await apiClient.PostAsync<object, AccessTokenResponse>(
            "/identity/auth/refresh",
            new { },
            cancellationToken);

        tokenStore.SetAccessToken(token.AccessToken);
        User = await apiClient.GetAsync<AuthenticatedUser>("/identity/me", cancellationToken);
        StateChanged?.Invoke();
    }

    private async Task EstablishAuthenticatedSessionAsync(AccessTokenResponse token, CancellationToken cancellationToken)
    {
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
