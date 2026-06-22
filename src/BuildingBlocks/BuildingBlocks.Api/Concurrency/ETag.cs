using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace BuildingBlocks.Api.Concurrency;

/// <summary>
/// Maps an EF rowversion concurrency token to/from a weak-free HTTP ETag for optimistic concurrency.
/// Editable MasterData resources return their rowversion as an ETag on reads; updates require a
/// matching <c>If-Match</c> header and return 409 on a mismatch.
/// </summary>
public static class ETag
{
    /// <summary>Formats a rowversion as a quoted base64 ETag value, e.g. <c>"AAAAAAAAB9E="</c>.</summary>
    public static string Format(byte[]? rowVersion) =>
        rowVersion is { Length: > 0 } ? $"\"{Convert.ToBase64String(rowVersion)}\"" : "\"\"";

    /// <summary>Parses a quoted base64 ETag value back to a rowversion. Returns null when absent/invalid.</summary>
    public static byte[]? Parse(string? etag)
    {
        if (string.IsNullOrWhiteSpace(etag))
            return null;

        var trimmed = etag.Trim().Trim('"');
        if (trimmed.Length == 0)
            return null;

        try
        {
            return Convert.FromBase64String(trimmed);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Reads and parses the request's <c>If-Match</c> header rowversion, if any.</summary>
    public static byte[]? GetIfMatch(this HttpRequest request)
    {
        var value = request.Headers[HeaderNames.IfMatch].ToString();
        return Parse(value);
    }
}
