using System.Text.Json;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Auth;

public sealed class ClientTokenRefresherTests
{
    [Fact]
    public async Task Consumed_cross_tab_refresh_retries_without_anonymous_failure()
    {
        var runtime = new ConsumedThenSuccessJsRuntime();
        var tokenStore = new AuthTokenStore();
        tokenStore.SetAccessToken("old-access-token");
        var refresher = new ClientTokenRefresher(
            runtime,
            tokenStore,
            new LocaleState(runtime));
        var failed = false;
        refresher.RefreshFailed += () => failed = true;

        var result = await refresher.TryRefreshAsync();

        result.ShouldBeTrue();
        runtime.CallCount.ShouldBe(2);
        tokenStore.AccessToken.ShouldBe("successor-access-token");
        failed.ShouldBeFalse();
    }

    private sealed class ConsumedThenSuccessJsRuntime : IJSRuntime
    {
        public int CallCount { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            object?[]? args) =>
            InvokeAsync<TValue>(
                identifier,
                CancellationToken.None,
                args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            CallCount++;
            if (CallCount == 1)
            {
                var body = JsonSerializer.Serialize(new
                {
                    code = ClientTokenRefresher.ConsumedRefreshTokenProblemCode
                });
                var error = JsonSerializer.Serialize(new
                {
                    status = 401,
                    body
                });
                throw new JSException(error);
            }

            var response = JsonSerializer.Serialize(new AccessTokenResponse(
                "successor-access-token",
                DateTimeOffset.UtcNow.AddMinutes(15)));
            return ValueTask.FromResult((TValue)(object)response);
        }
    }
}
