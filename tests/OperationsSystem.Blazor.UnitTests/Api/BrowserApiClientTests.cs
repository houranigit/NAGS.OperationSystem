using System.Text.Json;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Api;

public sealed class BrowserApiClientTests
{
    [Fact]
    public async Task Ata_chapter_status_updates_send_row_version_in_the_concurrency_header()
    {
        var runtime = new CapturingDownloadJsRuntime();
        var masterData = new MasterDataApiClient(NewClient(runtime));
        var id = Guid.NewGuid();

        await masterData.SetAtaChapterActiveAsync(id, false, "chapter-version");

        runtime.Arguments![0].ShouldBe("POST");
        runtime.Arguments[1].ShouldBe($"/masterdata/ata-chapters/{id}/deactivate");
        runtime.Arguments[2].ShouldBeNull();
        runtime.Arguments[5].ShouldBe("chapter-version");

        await masterData.SetAtaChapterCategoryActiveAsync(id, true, "category-version");

        runtime.Arguments[1].ShouldBe($"/masterdata/ata-chapter-categories/{id}/activate");
        runtime.Arguments[2].ShouldBeNull();
        runtime.Arguments[5].ShouldBe("category-version");
    }

    [Fact]
    public async Task GetAsync_returns_null_for_nullable_response_when_body_is_empty()
    {
        var api = NewClient(new StubJsRuntime(""));

        var result = await api.GetAsync<FlightDetail?>($"/operations/flights/{Guid.NewGuid()}");

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Flight_grid_query_preserves_filters_and_sort()
    {
        var runtime = new CapturingDownloadJsRuntime { Response = "{\"items\":[],\"page\":1,\"pageSize\":25,\"totalCount\":0}" };
        var operations = new OperationsApiClient(NewClient(runtime));
        var stationId = Guid.NewGuid();
        var fromUtc = DateTimeOffset.Parse("2026-10-30T05:00:00Z");

        await operations.GetFlightsAsync(1, 25, "123 & 456", stationId, fromUtc: fromUtc, sort: "sta desc");

        runtime.Arguments![1].ShouldBe(
            "/operations/flights?page=1&pageSize=25&search=123%20%26%20456" +
            $"&stationId={stationId}&fromUtc=2026-10-30T05%3A00%3A00.0000000%2B00%3A00&sort=sta%20desc");
    }

    [Fact]
    public async Task Change_user_access_forwards_role_and_row_version()
    {
        var runtime = new CapturingDownloadJsRuntime();
        var identity = new IdentityApiClient(NewClient(runtime));
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await identity.ChangeUserAccessAsync(
            userId,
            new AssignRoleRequest(roleId),
            "user-row-version");

        runtime.Identifier.ShouldBe("operationsSystem.api.request");
        runtime.Arguments.ShouldNotBeNull();
        runtime.Arguments![0].ShouldBe("PUT");
        runtime.Arguments[1].ShouldBe($"/identity/users/{userId}/role");
        ((AssignRoleRequest)runtime.Arguments[2]!).RoleId.ShouldBe(roleId);
        runtime.Arguments[5].ShouldBe("user-row-version");
    }

    [Fact]
    public async Task Direct_refresh_retries_once_when_another_tab_consumed_the_predecessor()
    {
        var runtime = new ConsumedThenSuccessJsRuntime();
        var api = NewClient(runtime);

        var token = await api.PostAsync<object, AccessTokenResponse>(
            "/identity/auth/refresh",
            new { });

        token.AccessToken.ShouldBe("successor-access-token");
        runtime.CallCount.ShouldBe(2);
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
        public string? Response { get; init; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            Identifier = identifier;
            Arguments = args;
            return ValueTask.FromResult(typeof(TValue) == typeof(string) && Response is not null
                ? (TValue)(object)Response
                : default!);
        }
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
