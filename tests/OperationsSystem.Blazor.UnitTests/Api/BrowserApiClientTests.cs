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

    [Fact]
    public async Task Approved_work_order_download_uses_authenticated_blob_download_route()
    {
        var runtime = new CapturingDownloadJsRuntime();
        var operations = new OperationsApiClient(NewClient(runtime));
        var flightId = Guid.NewGuid();

        await operations.DownloadApprovedWorkOrderAsync(flightId);

        runtime.Identifier.ShouldBe("operationsSystem.api.downloadFile");
        runtime.Arguments.ShouldNotBeNull();
        runtime.Arguments![0].ShouldBe($"/operations/flights/{flightId}/work-orders/approved/pdf");
        runtime.Arguments[1].ShouldBe("approved-work-order.pdf");
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

    private sealed class CapturingDownloadJsRuntime : IJSRuntime
    {
        public string? Identifier { get; private set; }
        public object?[]? Arguments { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            Identifier = identifier;
            Arguments = args;
            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
