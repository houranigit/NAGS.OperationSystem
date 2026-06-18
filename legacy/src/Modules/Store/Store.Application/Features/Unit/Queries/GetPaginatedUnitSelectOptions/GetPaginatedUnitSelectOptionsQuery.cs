using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Store.Contracts.Features.Unit;

namespace Store.Application.Features.Unit.Queries.GetPaginatedUnitSelectOptions;

public sealed record GetPaginatedUnitSelectOptionsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<UnitSelectOption>>;
