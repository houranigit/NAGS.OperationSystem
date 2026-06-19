using System.Text.Json;

namespace OperationsSystem.Blazor.Client.Api;

public sealed class ApiException : Exception
{
    public ApiException(int statusCode, string responseBody)
        : base($"API request failed with status code {statusCode}.")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }

    public string ResponseBody { get; }

    public bool IsForbidden => StatusCode == 403;

    public bool IsUnauthorized => StatusCode == 401;

    public bool IsNotFound => StatusCode == 404;

    /// <summary>Best-effort human-readable message extracted from the ProblemDetails body.</summary>
    public string ToDisplayMessage(string fallback)
    {
        if (string.IsNullOrWhiteSpace(ResponseBody))
            return fallback;

        try
        {
            using var document = JsonDocument.Parse(ResponseBody);
            var root = document.RootElement;

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in errors.EnumerateObject())
                {
                    if (field.Value.ValueKind == JsonValueKind.Array && field.Value.GetArrayLength() > 0)
                    {
                        var first = field.Value[0].GetString();
                        if (!string.IsNullOrWhiteSpace(first))
                            return first;
                    }
                }
            }

            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
            {
                var value = detail.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
            {
                var value = title.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }
        catch (JsonException)
        {
            // Fall through to the provided fallback.
        }

        return fallback;
    }
}
