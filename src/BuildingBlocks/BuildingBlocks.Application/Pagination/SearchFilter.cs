namespace BuildingBlocks.Application.Pagination;

public static class SearchFilter
{
    public static string? Term(string? search) =>
        string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToLowerInvariant();
}
