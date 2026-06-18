using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Store.Contracts.Features.GeneralSupport;

namespace Store.Application.Features.GeneralSupport.Queries.GetPaginatedGeneralSupports;

public sealed record GetPaginatedGeneralSupportsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<GeneralSupportDto>>;
