using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using Core.Contracts.Features.ManpowerType;

namespace Core.Application.Features.ManpowerType.Queries.GetPaginatedManpowerTypeSelectOptions;

public sealed record GetPaginatedManpowerTypeSelectOptionsQuery(
    int Page = 1,
    int PageSize = 20,
    string? FilterQuery = null,
    string? OrderByQuery = null,
    IReadOnlyList<string>? VisibleColumns = null
) : IQuery<PaginatedResult<ManpowerTypeSelectOption>>;
