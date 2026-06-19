using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace OperationsSystem.Blazor.Api;

public static class ApiProxyExtensions
{
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
        using var response = await httpClientFactory.CreateClient("ApiProxy")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

        context.Response.StatusCode = (int)response.StatusCode;
        CopyHeaders(response.Headers, context.Response.Headers);
        CopyHeaders(response.Content.Headers, context.Response.Headers);
        context.Response.Headers.Remove("transfer-encoding");

        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    private static Uri BuildTargetUri(HttpContext context, ApiProxyOptions options)
    {
        var baseUrl = options.BaseUrl.TrimEnd('/');
        var path = context.Request.RouteValues["path"]?.ToString() ?? string.Empty;
        return new Uri($"{baseUrl}/api/{path}{context.Request.QueryString}");
    }

    private static HttpRequestMessage CreateProxyRequest(HttpContext context, Uri targetUri)
    {
        var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

        if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
            request.Content = new StreamContent(context.Request.Body);

        foreach (var header in context.Request.Headers)
        {
            if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        return request;
    }

    private static void CopyHeaders(HttpHeaders source, IHeaderDictionary destination)
    {
        foreach (var header in source)
            destination[header.Key] = header.Value.ToArray();
    }
}
