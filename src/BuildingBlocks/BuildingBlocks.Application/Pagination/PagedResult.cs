namespace BuildingBlocks.Application.Pagination;

/// <summary>Standard envelope returned by list endpoints. Always server-side paginated.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages
    {
        get
        {
            if (PageSize <= 0 || TotalCount <= 0)
                return 0;

            var totalPages = ((TotalCount - 1) / PageSize) + 1;
            return totalPages > int.MaxValue ? int.MaxValue : (int)totalPages;
        }
    }

    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;

    public static PagedResult<T> Empty(int page, int pageSize) => new([], page, pageSize, 0);
}
