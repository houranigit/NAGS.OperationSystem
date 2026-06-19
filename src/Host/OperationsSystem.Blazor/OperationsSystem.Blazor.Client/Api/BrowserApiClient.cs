using System.Text.Json;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.State;

namespace OperationsSystem.Blazor.Client.Api;

/// <summary>
/// Transport for the backend API. Requests are issued through a browser <c>fetch</c> helper so the
/// httpOnly refresh cookie is sent automatically (server prerender is disabled, so JS interop is
/// always available). The in-memory access token is attached as a Bearer header.
/// </summary>
public sealed class BrowserApiClient(IJSRuntime jsRuntime, AuthTokenStore tokenStore, LocaleState locale)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<TResponse> GetAsync<TResponse>(string path, CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(HttpMethod.Get, path, body: null, cancellationToken);

    public Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(HttpMethod.Post, path, body, cancellationToken);

    public Task PostAsync<TRequest>(string path, TRequest body, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, path, body, cancellationToken);

    public Task PostAsync(string path, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, path, body: null, cancellationToken);

    public Task PutAsync<TRequest>(string path, TRequest body, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, path, body, cancellationToken);

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, path, body: null, cancellationToken);

    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(method, path, body, cancellationToken);
        return JsonSerializer.Deserialize<TResponse>(response, JsonOptions)
            ?? throw new JsonException($"The API response for '{path}' was empty.");
    }

    private async Task<string> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        try
        {
            return await jsRuntime.InvokeAsync<string>(
                "operationsSystem.api.request",
                cancellationToken,
                method.Method,
                path,
                body,
                tokenStore.AccessToken,
                locale.Language);
        }
        catch (JSException ex) when (TryReadApiError(ex.Message, out var statusCode, out var responseBody))
        {
            throw new ApiException(statusCode, responseBody);
        }
    }

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
