using OperationsSystem.Blazor.Api;
using Shouldly;
using Microsoft.AspNetCore.Http;

namespace OperationsSystem.Blazor.UnitTests.Api;

public sealed class ApiProxyExtensionsTests
{
    [Theory]
    [InlineData("Connection")]
    [InlineData("Keep-Alive")]
    [InlineData("Proxy-Authenticate")]
    [InlineData("Proxy-Authorization")]
    [InlineData("TE")]
    [InlineData("Trailer")]
    [InlineData("Transfer-Encoding")]
    [InlineData("Upgrade")]
    public void IsHopByHopHeader_matches_standard_proxy_owned_headers(string headerName)
    {
        ApiProxyExtensions.IsHopByHopHeader(headerName).ShouldBeTrue();
    }

    [Theory]
    [InlineData("X-Trace-Id")]
    [InlineData("X-Upstream-Timeout")]
    public void IsHopByHopHeader_matches_headers_named_by_connection_header(string headerName)
    {
        var connectionHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "X-Trace-Id",
            "X-Upstream-Timeout"
        };

        ApiProxyExtensions.IsHopByHopHeader(headerName, connectionHeaders).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Host")]
    [InlineData("Content-Length")]
    [InlineData("Connection")]
    public void IsProxyManagedRequestHeader_matches_request_headers_owned_by_the_proxy(string headerName)
    {
        ApiProxyExtensions.IsProxyManagedRequestHeader(headerName).ShouldBeTrue();
    }

    [Fact]
    public void IsProxyManagedRequestHeader_allows_end_to_end_headers()
    {
        ApiProxyExtensions.IsProxyManagedRequestHeader("Authorization").ShouldBeFalse();
        ApiProxyExtensions.IsProxyManagedRequestHeader("Content-Type").ShouldBeFalse();
    }

    [Fact]
    public void ApiProxyHttpClient_disables_handler_cookie_storage()
    {
        using var handler = ApiProxyHttpClient.CreatePrimaryHandler();

        var socketsHandler = handler.ShouldBeOfType<SocketsHttpHandler>();
        socketsHandler.UseCookies.ShouldBeFalse();
        socketsHandler.AllowAutoRedirect.ShouldBeFalse();
    }

    [Theory]
    [InlineData("v1/operations/flights/5cab7ecd-a547-47ea-9603-c603d1e5abe6/operations/batch", true)]
    [InlineData("v1/operations/flights/5cab7ecd-a547-47ea-9603-c603d1e5abe6/operations/returns-to-ramp/556ddd96-a407-4bd0-bb4c-02ebe795e518", true)]
    [InlineData("v1/operations/flights/5cab7ecd-a547-47ea-9603-c603d1e5abe6/operations/cancellation", false)]
    [InlineData("v1/operations/flights/5cab7ecd-a547-47ea-9603-c603d1e5abe6/operations-batch", false)]
    [InlineData("v1/operations/flights/invalid/operations/batch", false)]
    [InlineData("v1/operations/flights/5cab7ecd-a547-47ea-9603-c603d1e5abe6/operations/returns-to-ramp/invalid", false)]
    public void Portal_flight_upload_exemption_only_matches_batch_and_rtr_routes(string path, bool matches)
    {
        ApiProxyExtensions.IsPortalFlightUploadPath(path).ShouldBe(matches);
    }

    [Fact]
    public void Notifications_hub_target_preserves_path_query_and_converts_to_websocket_scheme()
    {
        var context = new DefaultHttpContext();
        context.Request.RouteValues["path"] = "negotiate";
        context.Request.QueryString = new QueryString("?negotiateVersion=1&access_token=token");

        var httpTarget = ApiProxyExtensions.BuildNotificationsHubTargetUri(
            context,
            new ApiProxyOptions { BaseUrl = "https://api.example.test/" });
        var webSocketTarget = ApiProxyExtensions.ToWebSocketUri(httpTarget);

        httpTarget.ToString().ShouldBe("https://api.example.test/hubs/notifications/negotiate?negotiateVersion=1&access_token=token");
        webSocketTarget.ToString().ShouldBe("wss://api.example.test/hubs/notifications/negotiate?negotiateVersion=1&access_token=token");
    }

}
