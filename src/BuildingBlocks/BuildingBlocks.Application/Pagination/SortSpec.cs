namespace BuildingBlocks.Application.Pagination;

/// <summary>
/// A parsed sort instruction in the API's <c>field:direction</c> form (e.g. <c>name:asc</c>,
/// <c>createdAt:desc</c>). <see cref="Field"/> is lower-cased so handlers can match it
/// case-insensitively against a whitelist of sortable columns.
/// </summary>
public readonly record struct SortSpec(string Field, bool Descending)
{
    /// <summary>
    /// Parses a <c>field:direction</c> string. Returns <c>null</c> for null/empty/blank input
    /// so callers fall back to their default ordering. Direction defaults to ascending.
    /// </summary>
    public static SortSpec? Parse(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return null;

        var parts = sort.Split(':', 2, StringSplitOptions.TrimEntries);
        var field = parts[0].ToLowerInvariant();
        if (field.Length == 0)
            return null;

        var descending = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
        return new SortSpec(field, descending);
    }
}
