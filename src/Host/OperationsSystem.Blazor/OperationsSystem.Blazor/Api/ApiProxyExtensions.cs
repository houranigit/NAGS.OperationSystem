using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Buffers;
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

    /// <summary>
    /// Same-origin proxy for the user-facing notifications hub. HTTP negotiate/long-poll requests
    /// use the existing safe HTTP proxy while WebSocket upgrades are bridged explicitly.
    /// </summary>
    public static IEndpointRouteBuilder MapNotificationsHubProxy(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("/hubs/notifications/{**path}", ProxiedMethods, ProxyNotificationsHubAsync)
            .AllowAnonymous()
            .DisableAntiforgery();
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

    private static async Task ProxyNotificationsHubAsync(
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        IOptions<ApiProxyOptions> options)
    {
        var targetUri = BuildNotificationsHubTargetUri(context, options.Value);
        if (context.WebSockets.IsWebSocketRequest)
        {
            await ProxyWebSocketAsync(context, targetUri);
            return;
        }

        try
        {
            using var request = CreateProxyRequest(context, targetUri);
            using var response = await httpClientFactory.CreateClient(ApiProxyHttpClient.Name)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

            context.Response.StatusCode = (int)response.StatusCode;
            CopyHeaders(response.Headers, context.Response.Headers);
            CopyHeaders(response.Content.Headers, context.Response.Headers);
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (HttpRequestException) when (!context.RequestAborted.IsCancellationRequested)
        {
            // SignalR's client owns retry/backoff. Surface an upstream outage as a gateway error
            // instead of an unhandled portal exception while the persisted inbox stays available.
            if (!context.Response.HasStarted)
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
        }
    }

    private static Uri BuildTargetUri(HttpContext context, ApiProxyOptions options)
    {
        var baseUrl = options.BaseUrl.Trim().TrimEnd('/');
        var path = context.Request.RouteValues["path"]?.ToString() ?? string.Empty;
        return new Uri($"{baseUrl}/api/{path}{context.Request.QueryString}");
    }

    internal static Uri BuildNotificationsHubTargetUri(HttpContext context, ApiProxyOptions options)
    {
        var baseUrl = options.BaseUrl.Trim().TrimEnd('/');
        var path = context.Request.RouteValues["path"]?.ToString()?.TrimStart('/');
        var suffix = string.IsNullOrEmpty(path) ? string.Empty : $"/{path}";
        return new Uri($"{baseUrl}/hubs/notifications{suffix}{context.Request.QueryString}");
    }

    internal static Uri ToWebSocketUri(Uri httpUri)
    {
        var builder = new UriBuilder(httpUri)
        {
            Scheme = string.Equals(httpUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? "wss" : "ws"
        };
        return builder.Uri;
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

    private static async Task ProxyWebSocketAsync(HttpContext context, Uri targetHttpUri)
    {
        using var upstream = new ClientWebSocket();
        CopyWebSocketRequestHeaders(context.Request.Headers, upstream.Options);

        foreach (var protocol in context.WebSockets.WebSocketRequestedProtocols)
            upstream.Options.AddSubProtocol(protocol);

        try
        {
            await upstream.ConnectAsync(ToWebSocketUri(targetHttpUri), context.RequestAborted);
        }
        catch (Exception) when (!context.Response.HasStarted)
        {
            if (!context.RequestAborted.IsCancellationRequested)
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        using var downstream = await context.WebSockets.AcceptWebSocketAsync(upstream.SubProtocol);
        using var bridgeCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);

        var downstreamToUpstream = PumpWebSocketAsync(downstream, upstream, bridgeCts.Token);
        var upstreamToDownstream = PumpWebSocketAsync(upstream, downstream, bridgeCts.Token);

        await Task.WhenAny(downstreamToUpstream, upstreamToDownstream);
        bridgeCts.Cancel();

        try
        {
            await Task.WhenAll(downstreamToUpstream, upstreamToDownstream);
        }
        catch (OperationCanceledException) when (bridgeCts.IsCancellationRequested)
        {
            // The opposite pump is canceled once either peer closes.
        }
        catch (WebSocketException)
        {
            // Either peer can disappear during navigation, shutdown, or a network transition.
            // The SignalR client reconnects; a reset is a normal bridge termination here.
        }
    }

    private static async Task PumpWebSocketAsync(WebSocket source, WebSocket destination, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (!cancellationToken.IsCancellationRequested && source.State is WebSocketState.Open or WebSocketState.CloseSent)
            {
                var result = await source.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    if (destination.State == WebSocketState.Open)
                    {
                        await destination.CloseOutputAsync(
                            result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                            result.CloseStatusDescription,
                            cancellationToken);
                    }
                    return;
                }

                if (destination.State != WebSocketState.Open)
                    return;

                await destination.SendAsync(
                    new ArraySegment<byte>(buffer, 0, result.Count),
                    result.MessageType,
                    result.EndOfMessage,
                    cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void CopyWebSocketRequestHeaders(IHeaderDictionary headers, ClientWebSocketOptions options)
    {
        foreach (var header in headers)
        {
            if (IsProxyManagedRequestHeader(header.Key) ||
                header.Key.StartsWith("Sec-WebSocket-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            options.SetRequestHeader(header.Key, header.Value.ToString());
        }
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
