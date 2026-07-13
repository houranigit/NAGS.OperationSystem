namespace OperationsSystem.Blazor.Client.Auth;

public sealed class AuthTokenStore
{
    public string? AccessToken { get; private set; }

    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    public void SetAccessToken(string? accessToken, DateTimeOffset? expiresAtUtc = null)
    {
        AccessToken = accessToken;
        ExpiresAtUtc = accessToken is null ? null : expiresAtUtc;
    }
}
