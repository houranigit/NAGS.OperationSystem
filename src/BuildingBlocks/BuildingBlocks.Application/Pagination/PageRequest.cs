namespace BuildingBlocks.Application.Pagination;

/// <summary>Normalizes list paging inputs before they are used in database queries.</summary>
public readonly record struct PageRequest(int Page, int PageSize, int Skip)
{
    public const int MaxPageSize = 100;

    public static PageRequest From(int page, int pageSize)
    {
        var normalizedPageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var maxPage = (int)Math.Min(int.MaxValue, ((long)int.MaxValue / normalizedPageSize) + 1);
        var normalizedPage = Math.Clamp(page, 1, maxPage);
        var skip = (normalizedPage - 1) * normalizedPageSize;

        return new PageRequest(normalizedPage, normalizedPageSize, skip);
    }

    public bool IsOutOfRange(long totalCount) => totalCount <= 0 || Skip >= totalCount;

    public PagedResult<T> Empty<T>(long totalCount = 0) => new([], Page, PageSize, totalCount);

    public PagedResult<T> ToResult<T>(IReadOnlyList<T> items, long totalCount) =>
        new(items, Page, PageSize, totalCount);
}
