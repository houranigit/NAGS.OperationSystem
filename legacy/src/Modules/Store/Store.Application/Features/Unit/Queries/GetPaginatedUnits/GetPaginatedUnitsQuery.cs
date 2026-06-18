using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Store.Contracts.Features.Unit;

namespace Store.Application.Features.Unit.Queries.GetPaginatedUnits;

public sealed record GetPaginatedUnitsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<UnitDto>>;
