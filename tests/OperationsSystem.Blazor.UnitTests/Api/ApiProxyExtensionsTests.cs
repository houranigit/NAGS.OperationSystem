using OperationsSystem.Blazor.Api;
using Shouldly;

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
}
