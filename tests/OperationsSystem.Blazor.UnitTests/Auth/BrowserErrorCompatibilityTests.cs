using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Auth;

public sealed class BrowserErrorCompatibilityTests
{
    private static readonly JsonSerializerOptions BrowserJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static TheoryData<string> BrowserStacks => new()
    {
        "",
        "\nError: API request failed\n    at execute (http://portal/js/api-client.js:100:15)",
        "\n@http://127.0.0.1:5172/js/api-client.gxl91o44am.js:83:24",
        "\nexecute@http://portal/js/api-client.js:100:24\nasyncFunctionResume@[native code]",
        "\nexecute@http://portal/js/api-client.js:100:15\nrequest@http://portal/js/api-client.js:123:18"
    };

    [Theory]
    [MemberData(nameof(BrowserStacks))]
    public void Api_error_details_survive_browser_specific_exception_stacks(string stack)
    {
        const string body = "{\"detail\":\"A {brace} and \\\"quoted\\\" value\\nremain intact\",\"code\":\"Identity.Auth.RefreshTokenMissing\"}";
        var message = ApiError(401, body, stack);

        var parsed = BrowserApiClient.TryReadApiError(message, out var status, out var responseBody);

        parsed.ShouldBeTrue();
        status.ShouldBe(401);
        responseBody.ShouldBe(body);
    }

    [Theory]
    [MemberData(nameof(BrowserStacks))]
    public async Task Fresh_browser_without_refresh_cookie_finishes_authorization_as_anonymous(string stack)
    {
        var runtime = new QueuedJsRuntime(new JSException(ApiError(
            401,
            "{\"code\":\"Identity.Auth.RefreshTokenMissing\"}",
            stack)));
        var tokenStore = new AuthTokenStore();
        tokenStore.SetAccessToken("stale-access-token");
        var locale = new LocaleState(runtime);
        var refresher = new ClientTokenRefresher(runtime, tokenStore, locale);
        var auth = new AuthSession(new BrowserApiClient(runtime, tokenStore, locale, refresher), tokenStore, refresher);
        using var provider = new PortalAuthStateProvider(auth);

        var state = await provider.GetAuthenticationStateAsync();

        state.User.Identity!.IsAuthenticated.ShouldBeFalse();
        auth.Status.ShouldBe(AuthStatus.Anonymous);
        auth.User.ShouldBeNull();
        tokenStore.AccessToken.ShouldBeNull();
        runtime.Paths.ShouldBe(["/identity/auth/refresh"]);
    }

    [Theory]
    [InlineData("TypeError: Load failed")]
    [InlineData("{not valid JSON}")]
    [InlineData("{}")]
    [InlineData("{\"status\":401}")]
    [InlineData("{\"status\":\"401\",\"body\":\"missing cookie\"}")]
    [InlineData("{\"status\":401,\"body\":{}}")]
    [InlineData("{\"status\":2147483648,\"body\":\"missing cookie\"}")]
    [InlineData("{\"status\":401,\"body\":\"unterminated")]
    public void Unrelated_or_malformed_js_errors_are_not_mistaken_for_api_errors(string message)
    {
        var parsed = BrowserApiClient.TryReadApiError(message, out var status, out var body);

        parsed.ShouldBeFalse();
        status.ShouldBe(0);
        body.ShouldBeEmpty();
    }

    [Fact]
    public async Task Safari_consumed_refresh_error_retries_with_the_successor_cookie()
    {
        var runtime = new QueuedJsRuntime(
            new JSException(ApiError(
                401,
                JsonSerializer.Serialize(new { code = ClientTokenRefresher.ConsumedRefreshTokenProblemCode }),
                "\nexecute@http://portal/js/api-client.js:100:24\nasyncFunctionResume@[native code]")),
            JsonSerializer.Serialize(new AccessTokenResponse("successor-access-token", DateTimeOffset.UtcNow.AddMinutes(15))));
        var tokenStore = new AuthTokenStore();
        var locale = new LocaleState(runtime);
        var refresher = new ClientTokenRefresher(runtime, tokenStore, locale);
        var api = new BrowserApiClient(runtime, tokenStore, locale, refresher);

        var token = await api.PostAsync<object, AccessTokenResponse>("/identity/auth/refresh", new { });

        token.AccessToken.ShouldBe("successor-access-token");
        runtime.Paths.ShouldBe(["/identity/auth/refresh", "/identity/auth/refresh"]);
    }

    private static string ApiError(int status, string body, string stack) =>
        JsonSerializer.Serialize(new { status, body }, BrowserJson) + stack;

    private sealed class QueuedJsRuntime(params object[] results) : IJSRuntime
    {
        private readonly Queue<object> _results = new(results);

        public List<string> Paths { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            identifier.ShouldBe("operationsSystem.api.request");
            Paths.Add((string)args![1]!);
            var result = _results.Dequeue();
            if (result is Exception error)
                throw error;

            return ValueTask.FromResult((TValue)result);
        }
    }
}
