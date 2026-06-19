namespace OperationsSystem.Blazor.Client.Auth;

public sealed class AuthTokenStore
{
    public string? AccessToken { get; private set; }

    public void SetAccessToken(string? accessToken) => AccessToken = accessToken;
}
