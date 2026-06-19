using System.Text.Json;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Auth;

namespace OperationsSystem.Blazor.Client.Api;

public sealed class BrowserApiClient(IJSRuntime jsRuntime, AuthTokenStore tokenStore)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TResponse> GetAsync<TResponse>(string path, CancellationToken cancellationToken = default) =>
        await SendAsync<TResponse>(HttpMethod.Get, path, body: null, cancellationToken);

    public async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        CancellationToken cancellationToken = default) =>
        await SendAsync<TResponse>(HttpMethod.Post, path, body, cancellationToken);

    public async Task PostAsync<TRequest>(string path, TRequest body, CancellationToken cancellationToken = default) =>
        await SendAsync(HttpMethod.Post, path, body, cancellationToken);

    public async Task PostAsync(string path, CancellationToken cancellationToken = default) =>
        await SendAsync(HttpMethod.Post, path, body: null, cancellationToken);

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
                "en");
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
