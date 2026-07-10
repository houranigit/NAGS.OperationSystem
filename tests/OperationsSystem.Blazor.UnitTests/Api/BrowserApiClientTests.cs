using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Api;

public sealed class BrowserApiClientTests
{
    [Fact]
    public async Task GetAsync_returns_null_for_nullable_response_when_body_is_empty()
    {
        var api = NewClient(new StubJsRuntime(""));

        var result = await api.GetAsync<WorkOrderSummaryModel?>($"/operations/flights/{Guid.NewGuid()}/work-orders/mine");

        result.ShouldBeNull();
    }

    private static BrowserApiClient NewClient(IJSRuntime jsRuntime)
    {
        var tokenStore = new AuthTokenStore();
        var locale = new LocaleState(jsRuntime);
        var refresher = new ClientTokenRefresher(jsRuntime, tokenStore, locale);
        return new BrowserApiClient(jsRuntime, tokenStore, locale, refresher);
    }

    private sealed class StubJsRuntime(string response) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (typeof(TValue) == typeof(string))
                return ValueTask.FromResult((TValue)(object)response);

            throw new InvalidOperationException($"Unexpected JS interop return type {typeof(TValue).Name}.");
        }
    }
}
