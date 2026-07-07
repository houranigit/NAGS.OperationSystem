using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace OperationsSystem.Blazor.Api;

public static class ApiProxyServiceCollectionExtensions
{
    public static IServiceCollection AddApiProxyHttpClient(this IServiceCollection services)
    {
        services.AddHttpClient(ApiProxyHttpClient.Name)
            .ConfigurePrimaryHttpMessageHandler(ApiProxyHttpClient.CreatePrimaryHandler);

        return services;
    }
}

internal static class ApiProxyHttpClient
{
    public const string Name = "ApiProxy";

    public static HttpMessageHandler CreatePrimaryHandler() =>
        new SocketsHttpHandler
        {
            // The proxy must pass each browser's Cookie/Set-Cookie headers through without keeping
            // a process-wide cookie jar. IHttpClientFactory pools handlers, so the default cookie
            // container can otherwise replay one browser's refresh token on another browser's request.
            UseCookies = false,
            AllowAutoRedirect = false
        };
}

public static class ApiProxyExtensions
{
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade"
    };

    private static readonly string[] ProxiedMethods =
    [
        HttpMethods.Get,
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete
    ];

    public static IEndpointRouteBuilder MapApiProxy(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("/api/{**path}", ProxiedMethods, ProxyAsync).AllowAnonymous();
        return endpoints;
    }

    private static async Task ProxyAsync(
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        IOptions<ApiProxyOptions> options)
    {
        var targetUri = BuildTargetUri(context, options.Value);
        using var request = CreateProxyRequest(context, targetUri);
        using var response = await httpClientFactory.CreateClient(ApiProxyHttpClient.Name)
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

        context.Response.StatusCode = (int)response.StatusCode;
        CopyHeaders(response.Headers, context.Response.Headers);
        CopyHeaders(response.Content.Headers, context.Response.Headers);

        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    private static Uri BuildTargetUri(HttpContext context, ApiProxyOptions options)
    {
        var baseUrl = options.BaseUrl.Trim().TrimEnd('/');
        var path = context.Request.RouteValues["path"]?.ToString() ?? string.Empty;
        return new Uri($"{baseUrl}/api/{path}{context.Request.QueryString}");
    }

    private static HttpRequestMessage CreateProxyRequest(HttpContext context, Uri targetUri)
    {
        var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

        if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
            request.Content = new StreamContent(context.Request.Body);

        var connectionHeaders = GetConnectionHeaderNames(context.Request.Headers.Connection.ToArray());
        foreach (var header in context.Request.Headers)
        {
            if (IsProxyManagedRequestHeader(header.Key, connectionHeaders))
                continue;

            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        return request;
    }

    private static void CopyHeaders(HttpHeaders source, IHeaderDictionary destination)
    {
        var connectionHeaders = GetConnectionHeaderNames(source);
        foreach (var header in source)
        {
            if (IsHopByHopHeader(header.Key, connectionHeaders))
                continue;

            destination[header.Key] = header.Value.ToArray();
        }
    }

    internal static bool IsProxyManagedRequestHeader(string headerName, IReadOnlySet<string>? connectionHeaders = null) =>
        string.Equals(headerName, "Host", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(headerName, "Content-Length", StringComparison.OrdinalIgnoreCase) ||
        IsHopByHopHeader(headerName, connectionHeaders);

    internal static bool IsHopByHopHeader(string headerName, IReadOnlySet<string>? connectionHeaders = null) =>
        HopByHopHeaders.Contains(headerName) ||
        (connectionHeaders?.Contains(headerName) ?? false);

    private static HashSet<string> GetConnectionHeaderNames(HttpHeaders headers)
    {
        return headers.TryGetValues("Connection", out var values)
            ? GetConnectionHeaderNames(values)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> GetConnectionHeaderNames(IEnumerable<string?> values)
    {
        var headerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            foreach (var headerName in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                headerNames.Add(headerName);
        }

        return headerNames;
    }
}
