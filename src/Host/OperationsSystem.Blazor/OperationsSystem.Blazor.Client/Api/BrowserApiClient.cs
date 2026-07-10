using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.State;

namespace OperationsSystem.Blazor.Client.Api;

/// <summary>
/// Transport for the backend API. Requests are issued through a browser <c>fetch</c> helper so the
/// httpOnly refresh cookie is sent automatically (server prerender is disabled, so JS interop is
/// always available). The in-memory access token is attached as a Bearer header.
/// </summary>
public sealed class BrowserApiClient(IJSRuntime jsRuntime, AuthTokenStore tokenStore, LocaleState locale, ClientTokenRefresher refresher)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public Task<TResponse> GetAsync<TResponse>(string path, CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(HttpMethod.Get, path, body: null, ifMatch: null, cancellationToken);

    public Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(HttpMethod.Post, path, body, ifMatch: null, cancellationToken);

    /// <summary>POST with optimistic concurrency that returns a body (e.g. add-child returning the new id).</summary>
    public Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, string? ifMatch, CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(HttpMethod.Post, path, body, ifMatch, cancellationToken);

    public Task PostAsync<TRequest>(string path, TRequest body, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, path, body, ifMatch: null, cancellationToken);

    /// <summary>POST with a request body and optimistic concurrency that returns no body.</summary>
    public Task PostAsync<TRequest>(string path, TRequest body, string? ifMatch, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, path, body, ifMatch, cancellationToken);

    public Task PostAsync(string path, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, path, body: null, ifMatch: null, cancellationToken);

    public Task PutAsync<TRequest>(string path, TRequest body, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, path, body, ifMatch: null, cancellationToken);

    /// <summary>PUT with optimistic concurrency: sends the rowversion as the <c>If-Match</c> header.</summary>
    public Task PutAsync<TRequest>(string path, TRequest body, string? ifMatch, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, path, body, ifMatch, cancellationToken);

    /// <summary>POST with optimistic concurrency (e.g. activate/deactivate of an editable record).</summary>
    public Task PostAsync(string path, string? ifMatch, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, path, body: null, ifMatch, cancellationToken);

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, path, body: null, ifMatch: null, cancellationToken);

    /// <summary>DELETE with optimistic concurrency.</summary>
    public Task DeleteAsync(string path, string? ifMatch, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, path, body: null, ifMatch, cancellationToken);

    public async Task UploadFileAsync(
        string path,
        byte[] content,
        string fileName,
        string contentType,
        string? ifMatch,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? fields = null)
    {
        try
        {
            await jsRuntime.InvokeAsync<string>("operationsSystem.api.uploadFile", cancellationToken,
                path, content, fileName, contentType, tokenStore.AccessToken, locale.Language, ifMatch, fields);
        }
        catch (JSException ex) when (TryReadApiError(ex.Message, out var statusCode, out var responseBody))
        {
            throw new ApiException(statusCode, responseBody);
        }
    }

    public async Task<BrowserFileContent> GetFileAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            return await jsRuntime.InvokeAsync<BrowserFileContent>("operationsSystem.api.requestFile", cancellationToken,
                path, tokenStore.AccessToken, locale.Language);
        }
        catch (JSException ex) when (TryReadApiError(ex.Message, out var statusCode, out var responseBody))
        {
            throw new ApiException(statusCode, responseBody);
        }
    }

    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        object? body,
        string? ifMatch,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(method, path, body, ifMatch, cancellationToken);
        return Deserialize<TResponse>(response);
    }

    private static TResponse Deserialize<TResponse>(string response) =>
        string.IsNullOrWhiteSpace(response)
            ? default!
            : JsonSerializer.Deserialize<TResponse>(response, JsonOptions)!;

    private async Task<string> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        string? ifMatch,
        CancellationToken cancellationToken)
    {
        try
        {
            return await InvokeAsync(method, path, body, ifMatch, cancellationToken);
        }
        catch (JSException ex) when (TryReadApiError(ex.Message, out var statusCode, out var responseBody))
        {
            // Transparently refresh once on a 401 (token expired mid-session) and retry. The refresh
            // is single-flight, so many concurrent 401s share one refresh. The refresh endpoint
            // itself is excluded to avoid recursion.
            var isRefresh = path.Contains("/auth/refresh", StringComparison.Ordinal);
            if (statusCode == 401 && !isRefresh && await refresher.TryRefreshAsync(cancellationToken))
            {
                try
                {
                    return await InvokeAsync(method, path, body, ifMatch, cancellationToken);
                }
                catch (JSException retryEx) when (TryReadApiError(retryEx.Message, out var retryStatus, out var retryBody))
                {
                    throw new ApiException(retryStatus, retryBody);
                }
            }

            throw new ApiException(statusCode, responseBody);
        }
    }

    private async Task<string> InvokeAsync(HttpMethod method, string path, object? body, string? ifMatch, CancellationToken cancellationToken) =>
        await jsRuntime.InvokeAsync<string>(
            "operationsSystem.api.request",
            cancellationToken,
            method.Method,
            path,
            body,
            tokenStore.AccessToken,
            locale.Language,
            ifMatch);

    private static bool TryReadApiError(string message, out int statusCode, out string responseBody)
    {
        statusCode = 0;
        responseBody = string.Empty;

        var jsonStart = message.IndexOf('{', StringComparison.Ordinal);
        if (jsonStart < 0)
            return false;

        try
        {
            var json = message[jsonStart..];
            var errorStart = json.IndexOf("\nError:", StringComparison.Ordinal);
            if (errorStart >= 0)
                json = json[..errorStart];

            using var document = JsonDocument.Parse(json);
            statusCode = document.RootElement.GetProperty("status").GetInt32();
            responseBody = document.RootElement.GetProperty("body").GetString() ?? string.Empty;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed record BrowserFileContent(string Base64, string ContentType);
