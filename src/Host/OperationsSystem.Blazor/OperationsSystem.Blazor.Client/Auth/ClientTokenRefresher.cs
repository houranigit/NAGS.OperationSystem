using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.State;

namespace OperationsSystem.Blazor.Client.Auth;

/// <summary>
/// Performs a single-flight access-token refresh against the httpOnly refresh cookie. Concurrent
/// callers (e.g. several API calls that all hit a 401 at once) share one in-flight refresh rather
/// than each issuing their own. Kept free of <see cref="AuthSession"/>/<c>BrowserApiClient</c>
/// dependencies to avoid a DI cycle; failures are surfaced via <see cref="RefreshFailed"/>.
/// </summary>
public sealed class ClientTokenRefresher(IJSRuntime jsRuntime, AuthTokenStore tokenStore, LocaleState locale)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _lock = new();
    private Task<bool>? _inFlight;

    /// <summary>Raised when a refresh attempt fails, so the session can drop to anonymous and sign out.</summary>
    public event Action? RefreshFailed;

    public Task<bool> TryRefreshAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_inFlight is { IsCompleted: false })
                return _inFlight;

            _inFlight = RefreshAsync(cancellationToken);
            return _inFlight;
        }
    }

    private async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await jsRuntime.InvokeAsync<string>(
                "operationsSystem.api.request",
                cancellationToken,
                HttpMethod.Post.Method,
                "/identity/auth/refresh",
                new { },
                (string?)null,
                locale.Language,
                (string?)null);

            var token = JsonSerializer.Deserialize<AccessTokenResponse>(json, JsonOptions);
            if (token is null)
            {
                Fail();
                return false;
            }

            tokenStore.SetAccessToken(token.AccessToken, token.ExpiresAtUtc);
            return true;
        }
        catch (Exception)
        {
            Fail();
            return false;
        }
        finally
        {
            lock (_lock)
                _inFlight = null;
        }
    }

    private void Fail()
    {
        tokenStore.SetAccessToken(null);
        RefreshFailed?.Invoke();
    }
}
