using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Store.Contracts.Features.Tool;

namespace Store.Application.Features.Tool.Queries.GetPaginatedTools;

public sealed record GetPaginatedToolsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<ToolDto>>;
